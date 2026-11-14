using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    public enum CarrierBlockShape
    {
        Rect1x1 = 0,
        Bar2x1 = 1,
        Bar1x2 = 2,
        LShape3 = 3,      // └
        JShape3 = 4,      // ┘
        LTopRight3 = 5,   // ┌ (Image 1 Red L)
        JTopLeft3 = 6,    // ┐ (Image 1 Blue L)
        TShape4 = 7,      // ┬ (Image 1 Green T)
        TShapeUp4 = 8,    // ┴
        Square2x2 = 9,
        Bar3x1 = 10
    }

    /// <summary>
    /// Shared geometry, SDF decomposition, and automatic socket layout math for polyomino Carrier Blocks.
    /// Operates in isotropic unit-cell local space (1 grid cell = 1.0 unit), centered on the shape's bounding box.
    /// </summary>
    public static class CarrierBlockShapeUtility
    {
        public const float WallInset = 0.088f;
        public const float OuterCornerRadius = 0.195f;
        public const float InnerCornerRadius = 0.125f;
        public const float OutlineThickness = 0.022f;
        public const float LipDepthOffset = 0.088f;

        private const float CellPadX = 0.055f;
        private const float CellPadY = 0.075f;
        private const float VerticalFrontBias = 0.024f;

        private static readonly Vector2Int[] CellsRect1x1    = { new Vector2Int(0, 0) };
        private static readonly Vector2Int[] CellsBar2x1     = { new Vector2Int(0, 0), new Vector2Int(1, 0) };
        private static readonly Vector2Int[] CellsBar1x2     = { new Vector2Int(0, 0), new Vector2Int(0, 1) };
        private static readonly Vector2Int[] CellsLShape3    = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 0) };
        private static readonly Vector2Int[] CellsJShape3    = { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] CellsLTopRight3 = { new Vector2Int(0, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] CellsJTopLeft3  = { new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] CellsTShape4    = { new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1), new Vector2Int(2, 1) };
        private static readonly Vector2Int[] CellsTShapeUp4  = { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] CellsSquare2x2  = { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1), new Vector2Int(1, 1) };
        private static readonly Vector2Int[] CellsBar3x1     = { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(2, 0) };

        public static Vector2Int[] GetCells(CarrierBlockShape shape)
        {
            switch (shape)
            {
                case CarrierBlockShape.Bar2x1:     return CellsBar2x1;
                case CarrierBlockShape.Bar1x2:     return CellsBar1x2;
                case CarrierBlockShape.LShape3:    return CellsLShape3;
                case CarrierBlockShape.JShape3:    return CellsJShape3;
                case CarrierBlockShape.LTopRight3: return CellsLTopRight3;
                case CarrierBlockShape.JTopLeft3:  return CellsJTopLeft3;
                case CarrierBlockShape.TShape4:    return CellsTShape4;
                case CarrierBlockShape.TShapeUp4:  return CellsTShapeUp4;
                case CarrierBlockShape.Square2x2:  return CellsSquare2x2;
                case CarrierBlockShape.Bar3x1:     return CellsBar3x1;
                case CarrierBlockShape.Rect1x1:
                default:                           return CellsRect1x1;
            }
        }

        public static void GetBounds(CarrierBlockShape shape, out int widthCells, out int heightCells)
        {
            switch (shape)
            {
                case CarrierBlockShape.Bar2x1:
                    widthCells = 2; heightCells = 1; break;
                case CarrierBlockShape.Bar1x2:
                    widthCells = 1; heightCells = 2; break;
                case CarrierBlockShape.LShape3:
                case CarrierBlockShape.JShape3:
                case CarrierBlockShape.LTopRight3:
                case CarrierBlockShape.JTopLeft3:
                case CarrierBlockShape.Square2x2:
                    widthCells = 2; heightCells = 2; break;
                case CarrierBlockShape.TShape4:
                case CarrierBlockShape.TShapeUp4:
                    widthCells = 3; heightCells = 2; break;
                case CarrierBlockShape.Bar3x1:
                    widthCells = 3; heightCells = 1; break;
                case CarrierBlockShape.Rect1x1:
                default:
                    widthCells = 1; heightCells = 1; break;
            }
        }

        public static int GetCellCount(CarrierBlockShape shape) => GetCells(shape).Length;

        public static string GetTurkishLabel(CarrierBlockShape shape)
        {
            switch (shape)
            {
                case CarrierBlockShape.Rect1x1:    return "1x1 Kare";
                case CarrierBlockShape.Bar2x1:     return "2x1 Yatay";
                case CarrierBlockShape.Bar1x2:     return "1x2 Dikey";
                case CarrierBlockShape.LShape3:    return "L Blok (└)";
                case CarrierBlockShape.JShape3:    return "J Blok (┘)";
                case CarrierBlockShape.LTopRight3: return "Üst L (┌)";
                case CarrierBlockShape.JTopLeft3:  return "Üst J (┐)";
                case CarrierBlockShape.TShape4:    return "T Blok (┬)";
                case CarrierBlockShape.TShapeUp4:  return "Ters T (┴)";
                case CarrierBlockShape.Square2x2:  return "2x2 Büyük";
                case CarrierBlockShape.Bar3x1:     return "3x1 Yatay";
                default:                           return shape.ToString();
            }
        }

        private static Vector4 BuildRectFromCellSpan(int minX, int minY, int maxX, int maxY, int totalW, int totalH)
        {
            float cx = (minX + maxX + 1) * 0.5f - totalW * 0.5f;
            float cy = (minY + maxY + 1) * 0.5f - totalH * 0.5f + VerticalFrontBias;
            float hx = (maxX - minX + 1) * 0.5f - CellPadX;
            float hy = (maxY - minY + 1) * 0.5f - CellPadY;
            return new Vector4(cx, cy, hx, hy);
        }

        /// <summary>
        /// Decomposes every supported polyomino shape into 1 or 2 overlapping axis-aligned rectangles
        /// in isotropic unit-cell local space, plus the exact Quad size wrapping it cleanly.
        /// </summary>
        public static void GetRectParts(
            CarrierBlockShape shape,
            out Vector4 rect0,
            out Vector4 rect1,
            out int rectCount,
            out float quadW,
            out float quadH)
        {
            GetBounds(shape, out int w, out int h);
            quadW = w + 0.16f;
            quadH = h + 0.16f;
            rect1 = Vector4.zero;

            switch (shape)
            {
                case CarrierBlockShape.Rect1x1:
                    rectCount = 1;
                    rect0 = BuildRectFromCellSpan(0, 0, 0, 0, w, h);
                    break;
                case CarrierBlockShape.Bar2x1:
                    rectCount = 1;
                    rect0 = BuildRectFromCellSpan(0, 0, 1, 0, w, h);
                    break;
                case CarrierBlockShape.Bar1x2:
                    rectCount = 1;
                    rect0 = BuildRectFromCellSpan(0, 0, 0, 1, w, h);
                    break;
                case CarrierBlockShape.Bar3x1:
                    rectCount = 1;
                    rect0 = BuildRectFromCellSpan(0, 0, 2, 0, w, h);
                    break;
                case CarrierBlockShape.Square2x2:
                    rectCount = 1;
                    rect0 = BuildRectFromCellSpan(0, 0, 1, 1, w, h);
                    break;
                case CarrierBlockShape.LShape3: // └
                    rectCount = 2;
                    rect0 = BuildRectFromCellSpan(0, 0, 0, 1, w, h);
                    rect1 = BuildRectFromCellSpan(0, 0, 1, 0, w, h);
                    break;
                case CarrierBlockShape.JShape3: // ┘
                    rectCount = 2;
                    rect0 = BuildRectFromCellSpan(0, 0, 1, 0, w, h);
                    rect1 = BuildRectFromCellSpan(1, 0, 1, 1, w, h);
                    break;
                case CarrierBlockShape.LTopRight3: // ┌
                    rectCount = 2;
                    rect0 = BuildRectFromCellSpan(0, 0, 0, 1, w, h);
                    rect1 = BuildRectFromCellSpan(0, 1, 1, 1, w, h);
                    break;
                case CarrierBlockShape.JTopLeft3: // ┐
                    rectCount = 2;
                    rect0 = BuildRectFromCellSpan(0, 1, 1, 1, w, h);
                    rect1 = BuildRectFromCellSpan(1, 0, 1, 1, w, h);
                    break;
                case CarrierBlockShape.TShape4: // ┬
                    rectCount = 2;
                    rect0 = BuildRectFromCellSpan(0, 1, 2, 1, w, h);
                    rect1 = BuildRectFromCellSpan(1, 0, 1, 1, w, h);
                    break;
                case CarrierBlockShape.TShapeUp4: // ┴
                    rectCount = 2;
                    rect0 = BuildRectFromCellSpan(0, 0, 2, 0, w, h);
                    rect1 = BuildRectFromCellSpan(1, 0, 1, 1, w, h);
                    break;
                default:
                    rectCount = 1;
                    rect0 = BuildRectFromCellSpan(0, 0, 0, 0, 1, 1);
                    break;
            }
        }

        public static Vector2 GetCellLocalCenter(CarrierBlockShape shape, Vector2Int cell)
        {
            GetBounds(shape, out int w, out int h);
            return new Vector2(cell.x + 0.5f - w * 0.5f, cell.y + 0.5f - h * 0.5f + VerticalFrontBias);
        }

        /// <summary>
        /// Automatically computes balanced gray socket coordinates (in isotropic block-local space),
        /// socket hole radius, and settled 3D ball local diameter inside the shape's white inner bed.
        /// </summary>
        public static void ComputeSocketLayoutForShape(
            CarrierBlockShape shape,
            int count,
            out Vector2[] positions,
            out float socketRadius,
            out float ballLocalDiameter)
        {
            int c = Mathf.Clamp(count, 1, MonsterCharacter.MaxVisualSockets);

            switch (shape)
            {
                case CarrierBlockShape.Rect1x1:
                    ComputeRect1x1Sockets(c, out positions, out socketRadius);
                    break;

                case CarrierBlockShape.Bar2x1:
                    ComputeLinearRectSockets(c, innerW: 1.71f, innerH: 0.67f, horizontal: true, out positions, out socketRadius);
                    break;

                case CarrierBlockShape.Bar3x1:
                    ComputeLinearRectSockets(c, innerW: 2.71f, innerH: 0.67f, horizontal: true, out positions, out socketRadius);
                    break;

                case CarrierBlockShape.Bar1x2:
                    ComputeLinearRectSockets(c, innerW: 0.71f, innerH: 1.67f, horizontal: false, out positions, out socketRadius);
                    break;

                case CarrierBlockShape.Square2x2:
                    ComputeGrid2x2Sockets(c, out positions, out socketRadius);
                    break;

                case CarrierBlockShape.LShape3:
                case CarrierBlockShape.JShape3:
                case CarrierBlockShape.LTopRight3:
                case CarrierBlockShape.JTopLeft3:
                    if (c <= 7)
                        ComputeLPolyominoSockets(shape, c, out positions, out socketRadius);
                    else
                        ComputePolyominoCellSockets(shape, c, out positions, out socketRadius);
                    break;

                case CarrierBlockShape.TShape4:
                case CarrierBlockShape.TShapeUp4:
                    if (c <= 8)
                        ComputeTPolyominoSockets(shape, c, out positions, out socketRadius);
                    else
                        ComputePolyominoCellSockets(shape, c, out positions, out socketRadius);
                    break;

                default:
                    ComputePolyominoCellSockets(shape, c, out positions, out socketRadius);
                    break;
            }

            ballLocalDiameter = socketRadius * 2.0f * 0.90f;
        }

        private static Vector2[] BuildOffsetGrid(int[] countsPerRow, float xStep, float[] rowYs, float yOffset)
        {
            int total = 0;
            for (int r = 0; r < countsPerRow.Length; r++) total += countsPerRow[r];
            Vector2[] pts = new Vector2[total];
            int idx = 0;
            for (int r = 0; r < countsPerRow.Length; r++)
            {
                int cols = countsPerRow[r];
                float y = rowYs[r] + yOffset;
                float startX = -0.5f * (cols - 1) * xStep;
                for (int i = 0; i < cols; i++)
                {
                    pts[idx++] = new Vector2(startX + i * xStep, y);
                }
            }
            return pts;
        }

        private static void ComputeRect1x1Sockets(int c, out Vector2[] positions, out float socketRadius)
        {
            const float scale = 0.68f;
            float yOff = VerticalFrontBias;
            switch (c)
            {
                case 1:
                    positions = new[] { new Vector2(0f, yOff) };
                    socketRadius = 0.165f;
                    return;
                case 2:
                    positions = new[] { new Vector2(-0.160f, yOff), new Vector2(0.160f, yOff) };
                    socketRadius = 0.122f;
                    return;
                case 3:
                    positions = new[]
                    {
                        new Vector2(-0.160f, -0.105f + yOff),
                        new Vector2( 0.160f, -0.105f + yOff),
                        new Vector2( 0.000f,  0.115f + yOff)
                    };
                    socketRadius = 0.106f;
                    return;
                case 4:
                    positions = BuildOffsetGrid(new[] { 2, 2 }, 0.284f, new[] { -0.132f, 0.132f }, yOff);
                    socketRadius = 0.095f;
                    return;
                case 5:
                    positions = BuildOffsetGrid(new[] { 3, 2 }, 0.195f, new[] { -0.115f, 0.105f }, yOff);
                    socketRadius = 0.080f;
                    return;
                case 6:
                    positions = BuildOffsetGrid(new[] { 3, 3 }, 0.195f, new[] { -0.115f, 0.105f }, yOff);
                    socketRadius = 0.078f;
                    return;
                case 7:
                    positions = BuildOffsetGrid(new[] { 4, 3 }, 0.152f, new[] { -0.105f, 0.095f }, yOff);
                    socketRadius = 0.064f;
                    return;
                case 8:
                    positions = BuildOffsetGrid(new[] { 4, 4 }, 0.152f, new[] { -0.105f, 0.095f }, yOff);
                    socketRadius = 0.064f;
                    return;
                default:
                    int cols = 4;
                    int rows = Mathf.CeilToInt(c / (float)cols);
                    int[] rowCounts = new int[rows];
                    int rem = c;
                    for (int r = 0; r < rows; r++)
                    {
                        int take = Mathf.Min(cols, rem);
                        rowCounts[r] = take;
                        rem -= take;
                    }
                    float[] ys = new float[rows];
                    float ySpan = 0.44f * scale * 1.4f;
                    float yStep = (rows > 1) ? ySpan / (rows - 1) : 0f;
                    for (int r = 0; r < rows; r++) ys[r] = -ySpan * 0.5f + r * yStep;
                    positions = BuildOffsetGrid(rowCounts, 0.145f, ys, yOff);
                    socketRadius = rows >= 4 ? 0.050f : 0.058f;
                    return;
            }
        }

        private static void ComputeLinearRectSockets(int c, float innerW, float innerH, bool horizontal, out Vector2[] positions, out float socketRadius)
        {
            float major = horizontal ? innerW : innerH;
            float minor = horizontal ? innerH : innerW;

            int singleRowMax = (major > 2.2f) ? 7 : 5;
            int rows = (c <= singleRowMax) ? 1 : (c <= singleRowMax * 2 ? 2 : 3);
            int perRowMax = Mathf.CeilToInt(c / (float)rows);

            List<Vector2> pts = new List<Vector2>(c);
            float usableMajor = major - 0.22f;
            float usableMinor = minor - 0.16f;

            if (rows == 1)
            {
                float maxSingleR = (c == 1) ? 0.190f : (c == 2 ? 0.175f : (c == 3 ? 0.160f : (c == 4 ? 0.132f : 0.118f)));
                float step = (c > 1) ? Mathf.Min(usableMajor / c, 0.86f) : 0f;
                float fitR = (c > 1) ? Mathf.Min(step * 0.38f, (usableMinor * 0.44f)) : maxSingleR;
                socketRadius = Mathf.Clamp(Mathf.Min(maxSingleR, fitR), 0.068f, 0.190f);

                float start = -0.5f * (c - 1) * step;
                for (int i = 0; i < c; i++)
                {
                    float u = start + i * step;
                    pts.Add(horizontal
                        ? new Vector2(u, VerticalFrontBias)
                        : new Vector2(0f, u + VerticalFrontBias));
                }
            }
            else
            {
                float maxRByMajor = usableMajor / (perRowMax * 2.52f);
                float maxRByMinor = usableMinor / (rows * 2.48f);
                socketRadius = Mathf.Clamp(Mathf.Min(maxRByMajor, maxRByMinor), 0.048f, 0.165f);

                int remaining = c;
                float stepMinor = usableMinor / rows;
                float startV = -0.5f * (rows - 1) * stepMinor;
                for (int r = 0; r < rows; r++)
                {
                    int colsInRow = Mathf.CeilToInt(remaining / (float)(rows - r));
                    remaining -= colsInRow;
                    float v = startV + r * stepMinor;
                    float stepMajor = (colsInRow > 1) ? Mathf.Min(usableMajor / colsInRow, 0.62f) : 0f;
                    float startU = -0.5f * (colsInRow - 1) * stepMajor;
                    for (int k = 0; k < colsInRow; k++)
                    {
                        float u = startU + k * stepMajor;
                        pts.Add(horizontal
                            ? new Vector2(u, v + VerticalFrontBias)
                            : new Vector2(v, u + VerticalFrontBias));
                    }
                }
            }

            positions = pts.ToArray();
        }

        private static void ComputeGrid2x2Sockets(int c, out Vector2[] positions, out float socketRadius)
        {
            switch (c)
            {
                case 1:
                    positions = new[] { new Vector2(0f, VerticalFrontBias) };
                    socketRadius = 0.21f;
                    return;
                case 2:
                    positions = new[]
                    {
                        new Vector2(-0.42f, VerticalFrontBias),
                        new Vector2( 0.42f, VerticalFrontBias)
                    };
                    socketRadius = 0.19f;
                    return;
                case 3:
                    positions = new[]
                    {
                        new Vector2(-0.42f, -0.34f + VerticalFrontBias),
                        new Vector2( 0.42f, -0.34f + VerticalFrontBias),
                        new Vector2( 0.00f,  0.36f + VerticalFrontBias)
                    };
                    socketRadius = 0.18f;
                    return;
                case 4:
                    positions = BuildOffsetGrid(new[] { 2, 2 }, 0.86f, new[] { -0.41f, 0.41f }, VerticalFrontBias);
                    socketRadius = 0.180f;
                    return;
                case 5:
                    positions = new[]
                    {
                        new Vector2(-0.46f, -0.44f + VerticalFrontBias),
                        new Vector2( 0.46f, -0.44f + VerticalFrontBias),
                        new Vector2( 0.00f,  VerticalFrontBias),
                        new Vector2(-0.46f,  0.44f + VerticalFrontBias),
                        new Vector2( 0.46f,  0.44f + VerticalFrontBias)
                    };
                    socketRadius = 0.155f;
                    return;
                case 6:
                    positions = BuildOffsetGrid(new[] { 3, 3 }, 0.49f, new[] { -0.39f, 0.39f }, VerticalFrontBias);
                    socketRadius = 0.148f;
                    return;
                case 7:
                    positions = new[]
                    {
                        new Vector2(-0.32f, -0.48f + VerticalFrontBias),
                        new Vector2( 0.32f, -0.48f + VerticalFrontBias),
                        new Vector2(-0.49f,  VerticalFrontBias),
                        new Vector2( 0.00f,  VerticalFrontBias),
                        new Vector2( 0.49f,  VerticalFrontBias),
                        new Vector2(-0.32f,  0.48f + VerticalFrontBias),
                        new Vector2( 0.32f,  0.48f + VerticalFrontBias)
                    };
                    socketRadius = 0.132f;
                    return;
                case 8:
                    positions = BuildOffsetGrid(new[] { 4, 4 }, 0.36f, new[] { -0.39f, 0.39f }, VerticalFrontBias);
                    socketRadius = 0.125f;
                    return;
                case 9:
                    positions = BuildOffsetGrid(new[] { 3, 3, 3 }, 0.48f, new[] { -0.47f, 0f, 0.47f }, VerticalFrontBias);
                    socketRadius = 0.125f;
                    return;
            }

            int cols = c <= 12 ? 4 : 4;
            int rows = Mathf.CeilToInt(c / (float)cols);
            const float inner = 1.48f;

            float xStep = (cols > 1) ? inner / cols : 0f;
            float yStep = (rows > 1) ? (inner - 0.06f) / rows : 0f;
            socketRadius = Mathf.Clamp(inner / (Mathf.Max(cols, rows) * 2.48f), 0.068f, 0.20f);

            List<Vector2> pts = new List<Vector2>(c);
            int remaining = c;
            float startY = 0.5f * (rows - 1) * yStep;
            for (int r = 0; r < rows; r++)
            {
                int inRow = Mathf.Min(cols, remaining);
                remaining -= inRow;
                float y = startY - r * yStep + VerticalFrontBias;
                float startX = -0.5f * (inRow - 1) * xStep;
                for (int col = 0; col < inRow; col++)
                {
                    pts.Add(new Vector2(startX + col * xStep, y));
                }
            }

            positions = pts.ToArray();
        }

        private static void ComputeLPolyominoSockets(
            CarrierBlockShape shape,
            int c,
            out Vector2[] positions,
            out float socketRadius)
        {
            Vector2Int[] cells = GetCells(shape);
            int cornerIdx = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                if (CountNeighbors(cells, cells[i]) == 2)
                {
                    cornerIdx = i;
                    break;
                }
            }

            Vector2Int cornerCell = cells[cornerIdx];
            Vector2Int armACell = cells[(cornerIdx + 1) % 3];
            Vector2Int armBCell = cells[(cornerIdx + 2) % 3];

            Vector2 cornerPt = GetCellLocalCenter(shape, cornerCell);
            Vector2 armAPt = GetCellLocalCenter(shape, armACell);
            Vector2 armBPt = GetCellLocalCenter(shape, armBCell);
            Vector2 dirA = (armAPt - cornerPt).normalized;
            Vector2 dirB = (armBPt - cornerPt).normalized;

            switch (c)
            {
                case 1:
                    positions = new[] { cornerPt };
                    socketRadius = 0.190f;
                    return;
                case 2:
                    positions = new[] { armAPt, armBPt };
                    socketRadius = 0.185f;
                    return;
                case 3:
                    positions = new[] { armAPt, cornerPt, armBPt };
                    socketRadius = 0.175f;
                    return;
                case 4:
                    positions = new[]
                    {
                        cornerPt + dirA * 1.07f,
                        cornerPt + dirA * 0.34f,
                        cornerPt + dirB * 0.34f,
                        cornerPt + dirB * 1.07f
                    };
                    socketRadius = 0.155f;
                    return;
                case 5:
                    positions = new[]
                    {
                        cornerPt + dirA * 1.08f,
                        cornerPt + dirA * 0.54f,
                        cornerPt,
                        cornerPt + dirB * 0.54f,
                        cornerPt + dirB * 1.08f
                    };
                    socketRadius = 0.145f;
                    return;
                case 6:
                    positions = new[]
                    {
                        cornerPt + dirA * 1.10f,
                        cornerPt + dirA * 0.69f,
                        cornerPt + dirA * 0.28f,
                        cornerPt + dirB * 0.28f,
                        cornerPt + dirB * 0.69f,
                        cornerPt + dirB * 1.10f
                    };
                    socketRadius = 0.128f;
                    return;
                default: // 7
                    positions = new[]
                    {
                        cornerPt + dirA * 1.11f,
                        cornerPt + dirA * 0.74f,
                        cornerPt + dirA * 0.37f,
                        cornerPt,
                        cornerPt + dirB * 0.37f,
                        cornerPt + dirB * 0.74f,
                        cornerPt + dirB * 1.11f
                    };
                    socketRadius = 0.116f;
                    return;
            }
        }

        private static void ComputeTPolyominoSockets(
            CarrierBlockShape shape,
            int c,
            out Vector2[] positions,
            out float socketRadius)
        {
            float barY = (shape == CarrierBlockShape.TShapeUp4 ? -0.5f : 0.5f) + VerticalFrontBias;
            float stemY = (shape == CarrierBlockShape.TShapeUp4 ? 0.5f : -0.5f) + VerticalFrontBias;
            float towardBar = Mathf.Sign(barY - stemY);

            if (c <= 4)
            {
                ComputePolyominoCellSockets(shape, c, out positions, out socketRadius);
                return;
            }

            List<Vector2> pts = new List<Vector2>(c);
            int barCount = (c == 5) ? 4 : (c == 6 ? 4 : (c == 7 ? 5 : 6));
            int stemCount = c - barCount;

            float barSpan = (barCount <= 4) ? 2.08f : 2.16f;
            float xStep = (barCount > 1) ? barSpan / (barCount - 1) : 0f;
            float startX = -0.5f * (barCount - 1) * xStep;
            for (int i = 0; i < barCount; i++)
            {
                pts.Add(new Vector2(startX + i * xStep, barY));
            }

            if (stemCount == 1)
            {
                pts.Add(new Vector2(0f, stemY));
                socketRadius = 0.155f;
            }
            else
            {
                pts.Add(new Vector2(0f, stemY - towardBar * 0.11f));
                pts.Add(new Vector2(0f, stemY + towardBar * 0.34f));
                socketRadius = (barCount <= 4) ? 0.140f : (barCount == 5 ? 0.132f : 0.125f);
            }

            positions = pts.ToArray();
        }

        private static void ComputePolyominoCellSockets(
            CarrierBlockShape shape,
            int c,
            out Vector2[] positions,
            out float socketRadius)
        {
            Vector2Int[] cells = GetCells(shape);
            int k = cells.Length;
            int baseCount = c / k;
            int extra = c % k;

            // Prioritize corner/hub cell(s) when distributing remainder sockets
            int[] alloc = new int[k];
            for (int i = 0; i < k; i++) alloc[i] = baseCount;

            // Count neighbor degree of each cell so junction/corner cells receive extra sockets smoothly first
            int[] order = new int[k];
            for (int i = 0; i < k; i++) order[i] = i;
            System.Array.Sort(order, (a, b) =>
            {
                int degA = CountNeighbors(cells, cells[a]);
                int degB = CountNeighbors(cells, cells[b]);
                return degB.CompareTo(degA);
            });
            for (int e = 0; e < extra; e++)
            {
                alloc[order[e % k]]++;
            }

            List<Vector2> pts = new List<Vector2>(c);
            float minRadius = 0.20f;

            for (int i = 0; i < k; i++)
            {
                int cellSockets = alloc[i];
                if (cellSockets <= 0) continue;

                Vector2 cellCenter = GetCellLocalCenter(shape, cells[i]);
                bool hasHorizNeighbor = HasNeighborX(cells, cells[i]);

                float cellR;
                switch (cellSockets)
                {
                    case 1:
                        pts.Add(cellCenter);
                        cellR = 0.185f;
                        break;
                    case 2:
                        if (hasHorizNeighbor)
                        {
                            pts.Add(cellCenter + new Vector2(-0.172f, 0f));
                            pts.Add(cellCenter + new Vector2( 0.172f, 0f));
                        }
                        else
                        {
                            pts.Add(cellCenter + new Vector2(0f, -0.165f));
                            pts.Add(cellCenter + new Vector2(0f,  0.165f));
                        }
                        cellR = 0.138f;
                        break;
                    case 3:
                        pts.Add(cellCenter + new Vector2(-0.155f, -0.112f));
                        pts.Add(cellCenter + new Vector2( 0.155f, -0.112f));
                        pts.Add(cellCenter + new Vector2( 0.000f,  0.122f));
                        cellR = 0.112f;
                        break;
                    default:
                        int subCols = cellSockets <= 4 ? 2 : 3;
                        int subRows = Mathf.CeilToInt(cellSockets / (float)subCols);
                        float xs = (subCols == 2) ? 0.25f : 0.19f;
                        float ys = (subRows == 2) ? 0.24f : 0.18f;
                        int rem = cellSockets;
                        for (int r = 0; r < subRows; r++)
                        {
                            int rowN = Mathf.Min(subCols, rem);
                            rem -= rowN;
                            float py = (subRows - 1) * 0.5f * ys - r * ys;
                            float sx = -(rowN - 1) * 0.5f * xs;
                            for (int col = 0; col < rowN; col++)
                            {
                                pts.Add(cellCenter + new Vector2(sx + col * xs, py));
                            }
                        }
                        cellR = (subCols <= 2 && subRows <= 2) ? 0.092f : 0.068f;
                        break;
                }

                if (cellR < minRadius) minRadius = cellR;
            }

            positions = pts.ToArray();
            socketRadius = minRadius;
        }

        private static int CountNeighbors(Vector2Int[] cells, Vector2Int target)
        {
            int n = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                int dx = Mathf.Abs(cells[i].x - target.x);
                int dy = Mathf.Abs(cells[i].y - target.y);
                if (dx + dy == 1) n++;
            }
            return n;
        }

        private static bool HasNeighborX(Vector2Int[] cells, Vector2Int target)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].y == target.y && Mathf.Abs(cells[i].x - target.x) == 1) return true;
            }
            return false;
        }
    }
}
