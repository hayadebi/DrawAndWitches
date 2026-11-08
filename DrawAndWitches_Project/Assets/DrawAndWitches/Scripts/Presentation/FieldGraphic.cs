using System;
using System.Collections.Generic;
using DrawAndWitches.Core;
using DrawAndWitches.Logic;
using UnityEngine;
using UnityEngine.UI;

namespace DrawAndWitches.Presentation
{
    /// <summary>
    /// 弾幕フィールドを1つのメッシュで描く uGUI Graphic。座標はロジックと同じワールド座標（720×960、y 下向き）。
    /// RectTransform は pivot 左上・サイズ 720×960 にし、表示倍率は localScale で調整する。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class FieldGraphic : MaskableGraphic
    {
        private const int CircleSegments = 28;
        private const int MaxVertices = 60000;
        private VertexHelper vh;
        private bool overflowWarned;

        /// <summary>毎フレームの描画処理（OnPopulateMesh 内で呼ばれる）</summary>
        public Action<FieldGraphic> Drawer;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = true;
        }

        public void Redraw()
        {
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper toFill)
        {
            toFill.Clear();
            if (Drawer == null) return;
            vh = toFill;
            try
            {
                Drawer(this);
            }
            catch (Exception e)
            {
                Debug.LogError("[FieldGraphic] draw failed: " + e);
                toFill.Clear();
            }
            finally
            {
                vh = null;
            }
        }

        private static Vector3 P(double x, double y)
        {
            return new Vector3((float)x, (float)-y, 0f);
        }

        private bool Room(int verts)
        {
            if (vh.currentVertCount + verts <= MaxVertices) return true;
            if (!overflowWarned)
            {
                overflowWarned = true;
                Debug.LogWarning("[FieldGraphic] vertex budget exceeded; some effects are skipped");
            }
            return false;
        }

        private void Vert(Vector3 p, Color c)
        {
            UIVertex v = UIVertex.simpleVert;
            v.position = p;
            v.color = c;
            vh.AddVert(v);
        }

        /// <summary>塗りつぶし円（edge は外周の色。中心と同色なら単色）</summary>
        public void Disc(double x, double y, double r, Color center, Color edge)
        {
            if (vh == null || r <= 0 || !Room(CircleSegments + 1)) return;
            int start = vh.currentVertCount;
            Vert(P(x, y), center);
            for (int i = 0; i < CircleSegments; i++)
            {
                double a = Math.PI * 2 * i / CircleSegments;
                Vert(P(x + Math.Cos(a) * r, y + Math.Sin(a) * r), edge);
            }
            for (int i = 0; i < CircleSegments; i++)
            {
                vh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % CircleSegments);
            }
        }

        public void Disc(double x, double y, double r, Color c)
        {
            Disc(x, y, r, c, c);
        }

        /// <summary>中心から外へ透明になる光</summary>
        public void Glow(double x, double y, double r, Color c)
        {
            Disc(x, y, r, c, new Color(c.r, c.g, c.b, 0f));
        }

        public void Ring(double x, double y, double r, double width, Color c)
        {
            if (vh == null || r <= 0 || !Room(CircleSegments * 2)) return;
            int start = vh.currentVertCount;
            double r0 = Math.Max(0, r - width * 0.5);
            double r1 = r + width * 0.5;
            for (int i = 0; i < CircleSegments; i++)
            {
                double a = Math.PI * 2 * i / CircleSegments;
                double cs = Math.Cos(a);
                double sn = Math.Sin(a);
                Vert(P(x + cs * r0, y + sn * r0), c);
                Vert(P(x + cs * r1, y + sn * r1), c);
            }
            for (int i = 0; i < CircleSegments; i++)
            {
                int a0 = start + i * 2;
                int b0 = start + ((i + 1) % CircleSegments) * 2;
                vh.AddTriangle(a0, a0 + 1, b0 + 1);
                vh.AddTriangle(a0, b0 + 1, b0);
            }
        }

        public void Line(double x0, double y0, double x1, double y1, double width, Color c0, Color c1)
        {
            if (vh == null || !Room(4)) return;
            double dx = x1 - x0;
            double dy = y1 - y0;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1e-6) return;
            double nx = -dy / len * width * 0.5;
            double ny = dx / len * width * 0.5;
            int s = vh.currentVertCount;
            Vert(P(x0 + nx, y0 + ny), c0);
            Vert(P(x0 - nx, y0 - ny), c0);
            Vert(P(x1 - nx, y1 - ny), c1);
            Vert(P(x1 + nx, y1 + ny), c1);
            vh.AddTriangle(s, s + 1, s + 2);
            vh.AddTriangle(s, s + 2, s + 3);
        }

        /// <summary>折れ線（各頂点に丸い継ぎ目を入れて隙間を防ぐ）。alphaAt で位置ごとの透明度を変えられる。</summary>
        public void Polyline(double[] xs, double[] ys, int count, double width, Color c, Func<int, float> alphaAt = null)
        {
            if (xs == null || ys == null || count < 2) return;
            for (int i = 0; i + 1 < count; i++)
            {
                Color c0 = c;
                Color c1 = c;
                if (alphaAt != null)
                {
                    c0.a *= alphaAt(i);
                    c1.a *= alphaAt(i + 1);
                }
                if (c0.a <= 0.003f && c1.a <= 0.003f) continue;
                Line(xs[i], ys[i], xs[i + 1], ys[i + 1], width, c0, c1);
            }
        }

        public void Diamond(double x, double y, double r, Color c)
        {
            if (vh == null || !Room(4)) return;
            int s = vh.currentVertCount;
            Vert(P(x, y - r), c);
            Vert(P(x + r, y), c);
            Vert(P(x, y + r), c);
            Vert(P(x - r, y), c);
            vh.AddTriangle(s, s + 1, s + 2);
            vh.AddTriangle(s, s + 2, s + 3);
        }

        public void RectFill(double x0, double y0, double x1, double y1, Color top, Color bottom)
        {
            if (vh == null || !Room(4)) return;
            int s = vh.currentVertCount;
            Vert(P(x0, y0), top);
            Vert(P(x1, y0), top);
            Vert(P(x1, y1), bottom);
            Vert(P(x0, y1), bottom);
            vh.AddTriangle(s, s + 1, s + 2);
            vh.AddTriangle(s, s + 2, s + 3);
        }
    }

    /// <summary>表示用の演出（ロジックには一切影響しない）。実時間で減衰する。</summary>
    public sealed class FieldEffects
    {
        private struct Particle
        {
            public float X, Y, Vx, Vy, Life, MaxLife, Size;
            public Color Color;
        }

        private struct Burst
        {
            public float X, Y, Age, Duration, Radius;
            public Color Color;
        }

        private readonly List<Particle> particles = new List<Particle>();
        private readonly List<Burst> bursts = new List<Burst>();
        private const int MaxParticles = 900;
        private System.Random rnd = new System.Random(7);

        public float Shake { get; private set; }
        public float EnemyFlash { get; private set; }
        public float PlayerFlash { get; private set; }

        public void Clear()
        {
            particles.Clear();
            bursts.Clear();
            Shake = 0;
            EnemyFlash = 0;
            PlayerFlash = 0;
            rnd = new System.Random(7);
        }

        private float R(float min, float max)
        {
            return min + (float)rnd.NextDouble() * (max - min);
        }

        public void Sparks(float x, float y, Color c, int count, float speed, float size, float life)
        {
            for (int i = 0; i < count && particles.Count < MaxParticles; i++)
            {
                float a = R(0, Mathf.PI * 2);
                float v = R(speed * 0.3f, speed);
                particles.Add(new Particle { X = x, Y = y, Vx = Mathf.Cos(a) * v, Vy = Mathf.Sin(a) * v, Life = life, MaxLife = life, Size = R(size * 0.5f, size), Color = c });
            }
        }

        public void AddBurst(float x, float y, float radius, float duration, Color c)
        {
            bursts.Add(new Burst { X = x, Y = y, Radius = radius, Duration = duration, Color = c });
        }

        /// <summary>ロジックのイベントから演出を作る</summary>
        public void OnSimEvent(SimEvent ev, BattleSimulation sim)
        {
            switch (ev.Type)
            {
                case SimEventType.Collide:
                    {
                        Bullet a = sim.Bullets[ev.A];
                        Bullet b = sim.Bullets[ev.B];
                        float x = (float)((a.X + b.X) * 0.5);
                        float y = (float)((a.Y + b.Y) * 0.5);
                        AddBurst(x, y, GameConstants.Explosion.Radius, 0.45f, new Color(1f, 0.85f, 0.5f, 0.9f));
                        Sparks(x, y, new Color(1f, 0.9f, 0.6f, 1f), 16, 340f, 5f, 0.5f);
                        Shake = Mathf.Max(Shake, 4f);
                        break;
                    }
                case SimEventType.Chain:
                    {
                        Bullet bu = sim.Bullets[ev.A];
                        Color c = FieldRenderer.BarrageColor(bu.Barrage);
                        AddBurst((float)bu.X, (float)bu.Y, GameConstants.Explosion.Radius, 0.4f, new Color(c.r, c.g, c.b, 0.85f));
                        Sparks((float)bu.X, (float)bu.Y, c, 10, 300f, 4f, 0.45f);
                        Shake = Mathf.Max(Shake, Mathf.Min(10f, 3f + ev.B));
                        break;
                    }
                case SimEventType.EnemyHit:
                    {
                        Bullet bu = sim.Bullets[ev.A];
                        Sparks((float)bu.X, (float)bu.Y, new Color(1f, 0.5f, 0.85f, 1f), 22, 420f, 6f, 0.6f);
                        EnemyFlash = 1f;
                        Shake = Mathf.Max(Shake, 7f);
                        break;
                    }
                case SimEventType.PlayerHit:
                case SimEventType.BaseHit:
                    {
                        Bullet bu = sim.Bullets[ev.A];
                        Sparks((float)bu.X, (float)bu.Y, new Color(1f, 0.35f, 0.4f, 1f), 30, 480f, 7f, 0.8f);
                        PlayerFlash = 1f;
                        Shake = Mathf.Max(Shake, 14f);
                        break;
                    }
                case SimEventType.Spawn:
                    {
                        Bullet bu = sim.Bullets[ev.A];
                        Sparks((float)bu.X, (float)bu.Y, FieldRenderer.BarrageColor(bu.Barrage), 4, 120f, 3f, 0.3f);
                        break;
                    }
                case SimEventType.ExecStart:
                    // 軌道線が光の粒になって散る
                    foreach (BarrageRuntime br in sim.Barrages)
                    {
                        Color c = FieldRenderer.BarrageColor(br.Index);
                        TrajectoryPath p = br.Path;
                        int step = Math.Max(1, p.Count / 24);
                        for (int i = 0; i < p.Count; i += step) Sparks((float)p.Xs[i], (float)p.Ys[i], c, 2, 90f, 3.5f, 0.7f);
                    }
                    break;
                default:
                    break;
            }
        }

        public void Update(float dt)
        {
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                Particle p = particles[i];
                p.Life -= dt;
                if (p.Life <= 0)
                {
                    particles.RemoveAt(i);
                    continue;
                }
                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
                p.Vx *= 1f - Mathf.Min(1f, 3.5f * dt);
                p.Vy *= 1f - Mathf.Min(1f, 3.5f * dt);
                particles[i] = p;
            }
            for (int i = bursts.Count - 1; i >= 0; i--)
            {
                Burst b = bursts[i];
                b.Age += dt;
                if (b.Age >= b.Duration)
                {
                    bursts.RemoveAt(i);
                    continue;
                }
                bursts[i] = b;
            }
            Shake = Mathf.Max(0f, Shake - dt * 40f);
            EnemyFlash = Mathf.Max(0f, EnemyFlash - dt * 3f);
            PlayerFlash = Mathf.Max(0f, PlayerFlash - dt * 2f);
        }

        public void Draw(FieldGraphic g)
        {
            foreach (Burst b in bursts)
            {
                float t = b.Age / b.Duration;
                float r = b.Radius * (0.4f + 0.8f * t);
                Color c = b.Color;
                c.a *= 1f - t;
                g.Glow(b.X, b.Y, r, new Color(c.r, c.g, c.b, c.a * 0.6f));
                g.Ring(b.X, b.Y, r, 4f * (1f - t) + 1f, c);
            }
            foreach (Particle p in particles)
            {
                float k = p.Life / p.MaxLife;
                Color c = p.Color;
                c.a *= k;
                g.Disc(p.X, p.Y, p.Size * (0.4f + 0.6f * k), c, new Color(c.r, c.g, c.b, 0f));
            }
        }
    }

    /// <summary>描画時の表示状態</summary>
    public sealed class FieldViewState
    {
        public int HoverBarrage = -1;
        public int HoverPoint = -1;
        public bool ShowPreview = true;
        public double PreviewTick;
        public float RevealAlpha = 1f;
        public float Time;
        public bool CursorVisible;
        public bool CursorPressed;
        public float CursorX;
        public float CursorY;
        public int ExecFadeTicks = 40;
    }

    /// <summary>BattleSimulation の状態をフィールドへ描く（Web版 renderer.js 相当）</summary>
    public static class FieldRenderer
    {
        private static readonly Color[] Palette =
        {
            Hex(0xff6fd8), Hex(0x5fd4ff), Hex(0xffd15c), Hex(0x8dff7a), Hex(0xc18cff), Hex(0xff8a5c), Hex(0x5cffd6), Hex(0xff5c7a),
            Hex(0xa3b8ff), Hex(0xf2ff5c), Hex(0xff9ef0), Hex(0x6effa0), Hex(0xffb35c), Hex(0x5c9bff), Hex(0xe0e0ff), Hex(0xff5cf0),
        };

        private static Color Hex(int rgb)
        {
            return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
        }

        private static readonly Dictionary<string, Color> TypeColorCache = new Dictionary<string, Color>(StringComparer.Ordinal);

        /// <summary>弾の色：弾種に color 指定（bullets.json）があればその色、無ければ弾幕ごとの色</summary>
        public static Color BulletColor(BattleSimulation sim, int barrageIndex)
        {
            if (barrageIndex >= 0 && barrageIndex < sim.Barrages.Length)
            {
                BulletTypeDef def = sim.Barrages[barrageIndex].Def;
                if (def != null && !string.IsNullOrEmpty(def.ColorHex))
                {
                    if (!TypeColorCache.TryGetValue(def.ColorHex, out Color tc))
                    {
                        tc = ColorUtility.TryParseHtmlString(def.ColorHex, out Color parsed) ? parsed : BarrageColor(barrageIndex);
                        TypeColorCache[def.ColorHex] = tc;
                    }
                    return tc;
                }
            }
            return BarrageColor(barrageIndex);
        }

        public static Color BarrageColor(int index)
        {
            return Palette[((index % Palette.Length) + Palette.Length) % Palette.Length];
        }

        private static Color A(Color c, float a)
        {
            return new Color(c.r, c.g, c.b, c.a * a);
        }

        /// <summary>自陣（結界）と自機・敵の判定円</summary>
        public static void DrawArena(FieldGraphic g, float time, float playerFlash, float enemyFlash)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(time * 2.2f);
            Color barrier = new Color(0.55f, 0.75f, 1f, 1f);
            g.RectFill(GameConstants.Base.X0, GameConstants.Base.Y0 - 30, GameConstants.Base.X1, GameConstants.Base.Y0, A(barrier, 0f), A(barrier, 0.22f + 0.1f * pulse));
            g.RectFill(GameConstants.Base.X0, GameConstants.Base.Y0, GameConstants.Base.X1, GameConstants.Base.Y1, A(barrier, 0.32f), A(barrier, 0.12f));
            g.Line(GameConstants.Base.X0, GameConstants.Base.Y0, GameConstants.Base.X1, GameConstants.Base.Y0, 3, A(barrier, 0.8f), A(barrier, 0.8f));
            Color pc = Color.Lerp(new Color(0.75f, 0.6f, 1f, 1f), new Color(1f, 0.3f, 0.4f, 1f), playerFlash);
            g.Glow(GameConstants.Player.X, GameConstants.Player.Y, GameConstants.Player.Radius * 2.2f, A(pc, 0.35f + 0.15f * pulse));
            g.Ring(GameConstants.Player.X, GameConstants.Player.Y, GameConstants.Player.Radius, 2.5f, A(pc, 0.9f));
            Color ec = Color.Lerp(new Color(1f, 0.55f, 0.85f, 1f), Color.white, enemyFlash);
            g.Glow(GameConstants.Enemy.X, GameConstants.Enemy.Y, GameConstants.Enemy.Radius * 1.8f, A(ec, 0.25f + 0.35f * enemyFlash));
            g.Ring(GameConstants.Enemy.X, GameConstants.Enemy.Y, GameConstants.Enemy.Radius, 2f, A(ec, 0.55f));
        }

        public static void Draw(FieldGraphic g, BattleSimulation sim, FieldViewState vs, FieldEffects fx)
        {
            DrawArena(g, vs.Time, fx != null ? fx.PlayerFlash : 0f, fx != null ? fx.EnemyFlash : 0f);
            if (sim == null) return;
            float reveal = Mathf.Clamp01(vs.RevealAlpha);
            bool edit = sim.Phase == BattlePhase.Edit;
            float pathAlpha = edit ? 1f : Mathf.Clamp01(1f - (float)sim.ExecTick / Math.Max(1, vs.ExecFadeTicks));
            pathAlpha *= reveal;

            // 軌道線
            if (pathAlpha > 0.01f)
            {
                foreach (BarrageRuntime br in sim.Barrages)
                {
                    Color c = BarrageColor(br.Index);
                    TrajectoryPath p = br.Path;
                    bool active = sim.DragActive && sim.DragBarrage == br.Index;
                    g.Polyline(p.Xs, p.Ys, p.Count, active ? 9 : 7, A(c, 0.16f * pathAlpha));
                    g.Polyline(p.Xs, p.Ys, p.Count, active ? 3.2 : 2.4, A(c, 0.85f * pathAlpha));
                    // 終端の方向矢印
                    double ex = p.Xs[p.Count - 1];
                    double ey = p.Ys[p.Count - 1];
                    g.Line(ex, ey, ex + p.EndDirX * 26, ey + p.EndDirY * 26, 2.4, A(c, 0.85f * pathAlpha), A(c, 0f));
                }
            }

            // ドラッグ範囲と起点
            if (edit && reveal > 0.01f)
            {
                if (sim.DragActive)
                {
                    IntPoint o = sim.Barrages[sim.DragBarrage].OrigPoints[sim.DragPoint];
                    Color c = BarrageColor(sim.DragBarrage);
                    g.Disc(o.X, o.Y, sim.DragRadius, A(c, 0.06f));
                    g.Ring(o.X, o.Y, sim.DragRadius, 2, A(c, 0.5f));
                    g.Diamond(o.X, o.Y, 5, A(c, 0.6f));
                }
                foreach (BarrageRuntime br in sim.Barrages)
                {
                    Color c = BarrageColor(br.Index);
                    IntPoint origin = br.Points[0];
                    g.Diamond(origin.X, origin.Y, 11, A(c, 0.9f * reveal));
                    g.Diamond(origin.X, origin.Y, 6, A(Color.white, 0.9f * reveal));
                    for (int pi = 1; pi < br.Points.Length; pi++)
                    {
                        IntPoint pt = br.Points[pi];
                        bool hover = vs.HoverBarrage == br.Index && vs.HoverPoint == pi;
                        bool grabbed = sim.DragActive && sim.DragBarrage == br.Index && sim.DragPoint == pi;
                        float pulse = 0.6f + 0.4f * Mathf.Sin(vs.Time * 5f + pi);
                        float r = grabbed ? 13f : (hover ? 12f : 9f);
                        g.Glow(pt.X, pt.Y, r * 2.4f, A(c, (hover || grabbed ? 0.6f : 0.3f * pulse) * reveal));
                        g.Disc(pt.X, pt.Y, r, A(c, reveal));
                        g.Disc(pt.X, pt.Y, r * 0.45f, A(Color.white, reveal));
                    }
                }
                if (vs.ShowPreview) DrawPreview(g, sim, vs.PreviewTick, reveal);
            }

            // 爆発の当たり範囲
            for (int e = sim.ExplosionCursor; e < sim.Explosions.Count; e++)
            {
                ExplosionData xp = sim.Explosions[e];
                int age = sim.ExecTick - xp.StartTick;
                if (age < 0 || age >= GameConstants.Explosion.ActiveTicks) continue;
                float k = 1f - (float)age / GameConstants.Explosion.ActiveTicks;
                g.Glow(xp.X, xp.Y, GameConstants.Explosion.Radius * 1.15f, new Color(1f, 0.9f, 0.6f, 0.45f * k));
            }

            // 弾
            foreach (Bullet bu in sim.Bullets)
            {
                if (bu.State != BulletState.Active) continue;
                Color c = BulletColor(sim, bu.Barrage);
                g.Glow(bu.X, bu.Y, bu.R * 2.6, A(c, 0.55f));
                g.Disc(bu.X, bu.Y, bu.R, A(c, 1f), A(Color.Lerp(c, Color.white, 0.2f), 1f));
                g.Disc(bu.X, bu.Y, bu.R * 0.55, Color.white, A(Color.white, 0.6f));
            }

            fx?.Draw(g);

            // bot・リプレイ操作のカーソル
            if (vs.CursorVisible)
            {
                g.Ring(vs.CursorX, vs.CursorY, vs.CursorPressed ? 14 : 18, 3, new Color(1f, 1f, 1f, 0.85f));
                g.Disc(vs.CursorX, vs.CursorY, 4, Color.white);
            }
        }

        /// <summary>編集中に弾の動きを薄く再生して見せる</summary>
        private static void DrawPreview(FieldGraphic g, BattleSimulation sim, double previewTick, float alpha)
        {
            double cycle = 0;
            foreach (BarrageRuntime br in sim.Barrages)
            {
                double last = br.StartDelayTicks + (br.Count - 1) * br.IntervalTicks;
                double travel = br.Path.Total / br.Def.Speed;
                if (last + travel > cycle) cycle = last + travel;
            }
            cycle = Math.Ceiling(cycle) + 40;
            double t = previewTick % cycle;
            foreach (Bullet bu in sim.Bullets)
            {
                BarrageRuntime br = sim.Barrages[bu.Barrage];
                double d = (t - bu.SpawnTick) * bu.Speed;
                if (d < 0 || d > br.Path.Total) continue;
                int seg = 0;
                br.Path.PositionAt(d, ref seg, out double x, out double y);
                g.Ring(x, y, bu.R, 2, A(BarrageColor(br.Index), 0.45f * alpha));
            }
        }
    }
}

/*
## このアセットの役割
FieldGraphic: 弾幕フィールド（軌道線・起点・弾・爆発・演出）を1つのメッシュで描く uGUI 部品。FieldEffects: 火花・爆発リング・画面揺れ・被弾フラッシュ。FieldRenderer: BattleSimulation の状態を描く処理（Web版 renderer.js / effects.js 相当）。

## 他アセットとの依存関係
BattleSimulation（読み取りのみ。描画はロジック結果に影響しません）。BattleScreen と EditorScreen が使用します。

## Inspector設定
なし（コードで生成）。

## USERがUnity上で行う作業
なし。

## 注意点
弾や線をスプライト画像に差し替えたい場合は、FieldRenderer.Draw の該当部分を Image のプール描画に置き換えてください。ロジック側の変更は不要です。
*/
