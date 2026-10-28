using System;
using System.Collections.Generic;
using UnityEngine;

namespace PuzzleGlass
{
    public static class ProceduralMeshUtils
    {
        /// <summary>
        /// Generates a rounded cube mesh with smoothed bevels for hypercasual candy-cube aesthetics.
        /// </summary>
        public static Mesh CreateRoundedCube(float size, float cornerRadius, int segments)
        {
            Mesh mesh = new Mesh();
            mesh.name = "RoundedCube";

            float half = size * 0.5f;
            float inner = Mathf.Max(0.001f, half - cornerRadius);

            int cornerRes = Mathf.Max(2, segments);
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<int> triangles = new List<int>();

            // Generate a subdivided normalized cube mapped to rounded corners
            int grid = cornerRes * 2;
            for (int face = 0; face < 6; face++)
            {
                int startIndex = vertices.Count;

                for (int y = 0; y <= grid; y++)
                {
                    float v = (float)y / grid;
                    float py = Mathf.Lerp(-half, half, v);

                    for (int x = 0; x <= grid; x++)
                    {
                        float u = (float)x / grid;
                        float px = Mathf.Lerp(-half, half, u);

                        Vector3 pointOnCube;
                        switch (face)
                        {
                            case 0: pointOnCube = new Vector3(px, py, half); break;  // Front
                            case 1: pointOnCube = new Vector3(half, py, -px); break; // Right
                            case 2: pointOnCube = new Vector3(-px, py, -half); break;// Back
                            case 3: pointOnCube = new Vector3(-half, py, px); break; // Left
                            case 4: pointOnCube = new Vector3(px, half, -py); break; // Top
                            default: pointOnCube = new Vector3(px, -half, py); break;// Bottom
                        }

                        // Round off points outside the inner box
                        Vector3 clamped = new Vector3(
                            Mathf.Clamp(pointOnCube.x, -inner, inner),
                            Mathf.Clamp(pointOnCube.y, -inner, inner),
                            Mathf.Clamp(pointOnCube.z, -inner, inner)
                        );

                        Vector3 diff = pointOnCube - clamped;
                        Vector3 normal = diff.sqrMagnitude > 0.0001f ? diff.normalized : (pointOnCube - Vector3.zero).normalized;
                        Vector3 vertex = clamped + normal * cornerRadius;

                        vertices.Add(vertex);
                        normals.Add(normal);
                    }
                }

                int stride = grid + 1;
                for (int y = 0; y < grid; y++)
                {
                    for (int x = 0; x < grid; x++)
                    {
                        int i0 = startIndex + y * stride + x;
                        int i1 = startIndex + (y + 1) * stride + x;
                        int i2 = startIndex + (y + 1) * stride + (x + 1);
                        int i3 = startIndex + y * stride + (x + 1);

                        triangles.Add(i0);
                        triangles.Add(i1);
                        triangles.Add(i2);

                        triangles.Add(i0);
                        triangles.Add(i2);
                        triangles.Add(i3);
                    }
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Generates an extruded tubular pipe mesh along a 3D spline/path.
        /// Perfect for the shaped glass rim border and handle loops!
        /// </summary>
        public static Mesh CreatePipeMesh(Vector3[] path, float radius, int radialSegments, bool isClosed)
        {
            Mesh mesh = new Mesh();
            mesh.name = "CurvedPipe";

            if (path == null || path.Length < 2) return mesh;

            int pathCount = path.Length;
            int ringCount = isClosed ? pathCount + 1 : pathCount;

            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();

            for (int i = 0; i < ringCount; i++)
            {
                int pIndex = i % pathCount;
                int nextIndex = (pIndex + 1) % pathCount;
                int prevIndex = (pIndex - 1 + pathCount) % pathCount;

                Vector3 curr = path[pIndex];
                Vector3 forward;

                if (isClosed)
                {
                    forward = (path[nextIndex] - path[prevIndex]).normalized;
                }
                else
                {
                    if (i == 0) forward = (path[1] - path[0]).normalized;
                    else if (i == ringCount - 1) forward = (path[pathCount - 1] - path[pathCount - 2]).normalized;
                    else forward = (path[i + 1] - path[i - 1]).normalized;
                }

                Vector3 up = Vector3.forward;
                if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.95f)
                {
                    up = Vector3.up;
                }
                Vector3 right = Vector3.Cross(forward, up).normalized;
                up = Vector3.Cross(right, forward).normalized;

                float v = (float)i / (ringCount - 1);

                for (int r = 0; r <= radialSegments; r++)
                {
                    float angle = (float)r / radialSegments * Mathf.PI * 2f;
                    Vector3 normal = (Mathf.Cos(angle) * right + Mathf.Sin(angle) * up).normalized;
                    Vector3 pos = curr + normal * radius;

                    vertices.Add(pos);
                    normals.Add(normal);
                    uvs.Add(new Vector2((float)r / radialSegments, v));
                }
            }

            int stride = radialSegments + 1;
            for (int i = 0; i < ringCount - 1; i++)
            {
                for (int r = 0; r < radialSegments; r++)
                {
                    int i0 = i * stride + r;
                    int i1 = (i + 1) * stride + r;
                    int i2 = (i + 1) * stride + (r + 1);
                    int i3 = i * stride + (r + 1);

                    triangles.Add(i0);
                    triangles.Add(i1);
                    triangles.Add(i2);

                    triangles.Add(i0);
                    triangles.Add(i2);
                    triangles.Add(i3);
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Creates a 2D triangulated planar mesh from a star-shaped closed loop of 2D/3D points.
        /// Perfect for front and back transparent glass panes of the shaped bottle!
        /// </summary>
        public static Mesh CreatePlanarMeshFromLoop(Vector3[] loop, float zOffset, bool facingForward, string meshName = "GlassPane")
        {
            Mesh mesh = new Mesh();
            mesh.name = meshName;
            if (loop == null || loop.Length < 3) return mesh;

            int n = loop.Length;
            List<Vector3> verts = new List<Vector3>(n + 1);
            List<Vector3> norms = new List<Vector3>(n + 1);
            List<Vector2> uvs = new List<Vector2>(n + 1);
            List<int> tris = new List<int>(n * 3);

            Vector3 normal = facingForward ? Vector3.forward : Vector3.back;

            // Find bounds for UV mapping
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (loop[i].x < minX) minX = loop[i].x;
                if (loop[i].x > maxX) maxX = loop[i].x;
                if (loop[i].y < minY) minY = loop[i].y;
                if (loop[i].y > maxY) maxY = loop[i].y;
            }
            float rangeX = Mathf.Max(0.001f, maxX - minX);
            float rangeY = Mathf.Max(0.001f, maxY - minY);

            // Center vertex at index 0
            verts.Add(new Vector3(0f, 0f, zOffset));
            norms.Add(normal);
            uvs.Add(new Vector2((0f - minX) / rangeX, (0f - minY) / rangeY));

            // Perimeter vertices
            for (int i = 0; i < n; i++)
            {
                Vector3 p = new Vector3(loop[i].x, loop[i].y, zOffset);
                verts.Add(p);
                norms.Add(normal);
                uvs.Add(new Vector2((p.x - minX) / rangeX, (p.y - minY) / rangeY));
            }

            // Triangles fanning out from center
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                int iCurr = i + 1;
                int iNext = next + 1;

                if (facingForward)
                {
                    tris.Add(0);
                    tris.Add(iCurr);
                    tris.Add(iNext);
                }
                else
                {
                    tris.Add(0);
                    tris.Add(iNext);
                    tris.Add(iCurr);
                }
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Creates a solid, extruded 3D volume mesh from a 2D closed polygon loop.
        /// Generates front face, back face, and smooth perimeter sidewalls connecting them.
        /// Perfect for a clean, borderless, modern 3D glass container!
        /// </summary>
        public static Mesh CreateExtrudedVolume(Vector3[] loop, float depth, string meshName = "GlassContainer3D")
        {
            Mesh mesh = new Mesh();
            mesh.name = meshName;
            if (loop == null || loop.Length < 3) return mesh;

            int n = loop.Length;
            float half = depth * 0.5f;

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            // Find bounds for UV mapping
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                if (loop[i].x < minX) minX = loop[i].x;
                if (loop[i].x > maxX) maxX = loop[i].x;
                if (loop[i].y < minY) minY = loop[i].y;
                if (loop[i].y > maxY) maxY = loop[i].y;
            }
            float rangeX = Mathf.Max(0.001f, maxX - minX);
            float rangeY = Mathf.Max(0.001f, maxY - minY);

            // 1. Front Face (+half)
            int frontCenterIdx = verts.Count;
            verts.Add(new Vector3(0f, 0f, half));
            norms.Add(Vector3.forward);
            uvs.Add(new Vector2((0f - minX) / rangeX, (0f - minY) / rangeY));

            int frontRingStart = verts.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = new Vector3(loop[i].x, loop[i].y, half);
                verts.Add(p);
                norms.Add(Vector3.forward);
                uvs.Add(new Vector2((p.x - minX) / rangeX, (p.y - minY) / rangeY));
            }

            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                tris.Add(frontCenterIdx);
                tris.Add(frontRingStart + i);
                tris.Add(frontRingStart + next);
            }

            // 2. Back Face (-half)
            int backCenterIdx = verts.Count;
            verts.Add(new Vector3(0f, 0f, -half));
            norms.Add(Vector3.back);
            uvs.Add(new Vector2((0f - minX) / rangeX, (0f - minY) / rangeY));

            int backRingStart = verts.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = new Vector3(loop[i].x, loop[i].y, -half);
                verts.Add(p);
                norms.Add(Vector3.back);
                uvs.Add(new Vector2((p.x - minX) / rangeX, (p.y - minY) / rangeY));
            }

            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                tris.Add(backCenterIdx);
                tris.Add(backRingStart + next);
                tris.Add(backRingStart + i);
            }

            // 3. Perimeter Sidewalls connecting Front and Back
            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;
                Vector3 p0 = loop[i];
                Vector3 p1 = loop[next];

                Vector3 edge = (p1 - p0).normalized;
                Vector3 wallNormal = Vector3.Cross(edge, Vector3.forward).normalized;

                int baseIdx = verts.Count;
                verts.Add(new Vector3(p0.x, p0.y, half));  norms.Add(wallNormal); uvs.Add(new Vector2(0f, 1f));
                verts.Add(new Vector3(p0.x, p0.y, -half)); norms.Add(wallNormal); uvs.Add(new Vector2(0f, 0f));
                verts.Add(new Vector3(p1.x, p1.y, -half)); norms.Add(wallNormal); uvs.Add(new Vector2(1f, 0f));
                verts.Add(new Vector3(p1.x, p1.y, half));  norms.Add(wallNormal); uvs.Add(new Vector2(1f, 1f));

                tris.Add(baseIdx);
                tris.Add(baseIdx + 1);
                tris.Add(baseIdx + 2);

                tris.Add(baseIdx);
                tris.Add(baseIdx + 2);
                tris.Add(baseIdx + 3);
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Generates a tapered pipe mesh whose radius smoothly grows and tapers at the ends.
        /// Ideal for stylized glass reflection highlight arcs!
        /// </summary>
        public static Mesh CreateTaperedPipeMesh(Vector3[] path, float maxRadius, int radialSegments)
        {
            Mesh mesh = new Mesh();
            mesh.name = "TaperedHighlightArc";
            if (path == null || path.Length < 2) return mesh;

            int pathCount = path.Length;
            List<Vector3> vertices = new List<Vector3>();
            List<Vector3> normals = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> triangles = new List<int>();

            for (int i = 0; i < pathCount; i++)
            {
                Vector3 curr = path[i];
                Vector3 forward;
                if (i == 0) forward = (path[1] - path[0]).normalized;
                else if (i == pathCount - 1) forward = (path[pathCount - 1] - path[pathCount - 2]).normalized;
                else forward = (path[i + 1] - path[i - 1]).normalized;

                Vector3 up = Vector3.forward;
                if (Mathf.Abs(Vector3.Dot(forward, up)) > 0.95f) up = Vector3.up;
                Vector3 right = Vector3.Cross(forward, up).normalized;
                up = Vector3.Cross(right, forward).normalized;

                float t = (float)i / (pathCount - 1);
                // Sinusoidal taper: 0 at tips, maxRadius in the center
                float radius = Mathf.Max(0.005f, maxRadius * Mathf.Sin(t * Mathf.PI));

                for (int r = 0; r <= radialSegments; r++)
                {
                    float angle = (float)r / radialSegments * Mathf.PI * 2f;
                    Vector3 normal = (Mathf.Cos(angle) * right + Mathf.Sin(angle) * up).normalized;
                    Vector3 pos = curr + normal * radius;

                    vertices.Add(pos);
                    normals.Add(normal);
                    uvs.Add(new Vector2((float)r / radialSegments, t));
                }
            }

            int stride = radialSegments + 1;
            for (int i = 0; i < pathCount - 1; i++)
            {
                for (int r = 0; r < radialSegments; r++)
                {
                    int i0 = i * stride + r;
                    int i1 = (i + 1) * stride + r;
                    int i2 = (i + 1) * stride + (r + 1);
                    int i3 = i * stride + (r + 1);

                    triangles.Add(i0);
                    triangles.Add(i1);
                    triangles.Add(i2);

                    triangles.Add(i0);
                    triangles.Add(i2);
                    triangles.Add(i3);
                }
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Generates a closed circular tubular pipe at specified center and radius.
        /// Perfect for the bottle mouth collar lip!
        /// </summary>
        public static Mesh CreateCircularPipe(Vector3 center, float ringRadius, float pipeRadius, int ringSegments, int radialSegments)
        {
            Vector3[] circlePath = new Vector3[ringSegments];
            for (int i = 0; i < ringSegments; i++)
            {
                float angle = (float)i / ringSegments * Mathf.PI * 2f;
                circlePath[i] = center + new Vector3(Mathf.Cos(angle) * ringRadius, Mathf.Sin(angle) * ringRadius, 0f);
            }
            return CreatePipeMesh(circlePath, pipeRadius, radialSegments, true);
        }

        /// <summary>
        /// Creates a rounded pill/capsule-like flat badge mesh for counter numbers.
        /// </summary>
        public static Mesh CreatePillBadge(float width, float height, float thickness, float cornerRadius)
        {
            Mesh mesh = new Mesh();
            mesh.name = "PillBadge";

            int segments = 16;
            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<int> tris = new List<int>();

            float halfW = width * 0.5f;
            float halfH = height * 0.5f;
            float innerW = Mathf.Max(0.01f, halfW - cornerRadius);
            float innerH = Mathf.Max(0.01f, halfH - cornerRadius);
            float halfT = thickness * 0.5f;

            // Generate 2D rounded rectangle perimeter points
            List<Vector2> contour = new List<Vector2>();

            // Corner centers: TR, TL, BL, BR
            Vector2[] centers = new Vector2[] {
                new Vector2(innerW, innerH),
                new Vector2(-innerW, innerH),
                new Vector2(-innerW, -innerH),
                new Vector2(innerW, -innerH)
            };

            for (int c = 0; c < 4; c++)
            {
                float startAngle = c * Mathf.PI * 0.5f;
                for (int s = 0; s <= segments; s++)
                {
                    float a = startAngle + (float)s / segments * Mathf.PI * 0.5f;
                    contour.Add(centers[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * cornerRadius);
                }
            }

            int count = contour.Count;

            // Front face vertices
            int frontCenterIdx = verts.Count;
            verts.Add(new Vector3(0, 0, halfT));
            norms.Add(Vector3.forward);

            int frontRingStart = verts.Count;
            for (int i = 0; i < count; i++)
            {
                verts.Add(new Vector3(contour[i].x, contour[i].y, halfT));
                norms.Add(Vector3.forward);
            }

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                tris.Add(frontCenterIdx);
                tris.Add(frontRingStart + i);
                tris.Add(frontRingStart + next);
            }

            // Back face vertices
            int backCenterIdx = verts.Count;
            verts.Add(new Vector3(0, 0, -halfT));
            norms.Add(Vector3.back);

            int backRingStart = verts.Count;
            for (int i = 0; i < count; i++)
            {
                verts.Add(new Vector3(contour[i].x, contour[i].y, -halfT));
                norms.Add(Vector3.back);
            }

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                tris.Add(backCenterIdx);
                tris.Add(backRingStart + next);
                tris.Add(backRingStart + i);
            }

            // Sides connecting front and back
            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % count;
                Vector2 p0 = contour[i];
                Vector2 p1 = contour[next];
                Vector2 edgeNormal = new Vector2(p1.y - p0.y, -(p1.x - p0.x)).normalized;
                Vector3 n = new Vector3(edgeNormal.x, edgeNormal.y, 0f);

                int v0 = verts.Count;
                int v1 = v0 + 1;
                int v2 = v0 + 2;
                int v3 = v0 + 3;
                verts.Add(new Vector3(p0.x, p0.y, halfT)); norms.Add(n);
                verts.Add(new Vector3(p0.x, p0.y, -halfT)); norms.Add(n);
                verts.Add(new Vector3(p1.x, p1.y, -halfT)); norms.Add(n);
                verts.Add(new Vector3(p1.x, p1.y, halfT)); norms.Add(n);

                tris.Add(v0); tris.Add(v1); tris.Add(v2);
                tris.Add(v0); tris.Add(v2); tris.Add(v3);
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Generates a concave hollow mouth cavity mesh (deep bowl opening at Z=0 and recessing inward into +Z).
        /// </summary>
        public static Mesh CreateMouthCavity(float width, float height, float depth, int segments)
        {
            Mesh mesh = new Mesh();
            mesh.name = "MouthCavity";

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            int rings = segments;
            int cols = segments * 2;

            for (int r = 0; r <= rings; r++)
            {
                float v = (float)r / rings;
                // v = 0 is mouth rim opening (radFactor = 1, z = 0)
                // v = 1 is back apex of the mouth (radFactor = 0, z = depth)
                float theta = (1f - v) * Mathf.PI * 0.5f; // PI/2 down to 0
                float radFactor = Mathf.Sin(theta);
                float z = (1f - Mathf.Cos(theta)) * depth;

                for (int c = 0; c <= cols; c++)
                {
                    float u = (float)c / cols;
                    float phi = u * Mathf.PI * 2f;

                    float x = Mathf.Cos(phi) * (width * 0.5f) * radFactor;
                    float y = Mathf.Sin(phi) * (height * 0.5f) * radFactor;

                    Vector3 pos = new Vector3(x, y, z);
                    // Normal points inward toward cavity center / camera
                    Vector3 norm = new Vector3(-x, -y, -z * 0.5f - 0.2f).normalized;

                    verts.Add(pos);
                    norms.Add(norm);
                    uvs.Add(new Vector2(u, v));
                }
            }

            int stride = cols + 1;
            for (int r = 0; r < rings; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int i0 = r * stride + c;
                    int i1 = (r + 1) * stride + c;
                    int i2 = (r + 1) * stride + (c + 1);
                    int i3 = r * stride + (c + 1);

                    // Front facing (seen from inside mouth looking out)
                    tris.Add(i0); tris.Add(i1); tris.Add(i2);
                    tris.Add(i0); tris.Add(i2); tris.Add(i3);

                    // Back facing (seen from camera looking into mouth)
                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i0); tris.Add(i3); tris.Add(i2);
                }
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Generates a thin vertical glass ribbon wall extruded upwards from a base path.
        /// Double-sided for crystal glass rendering.
        /// </summary>
        public static Mesh CreateGlassRibbonWall(Vector3[] basePath, float height, Vector3 upDirection, bool isClosed)
        {
            Mesh mesh = new Mesh();
            mesh.name = "GlassRibbonWall";

            if (basePath == null || basePath.Length < 2) return mesh;

            int n = basePath.Length;
            int count = isClosed ? n : n - 1;

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            for (int i = 0; i < count; i++)
            {
                int next = (i + 1) % n;
                Vector3 b0 = basePath[i];
                Vector3 b1 = basePath[next];
                Vector3 t0 = b0 + upDirection * height;
                Vector3 t1 = b1 + upDirection * height;

                Vector3 forward = (b1 - b0).normalized;
                Vector3 normal = Vector3.Cross(forward, upDirection).normalized;

                int vStart = verts.Count;
                verts.Add(b0); norms.Add(normal); uvs.Add(new Vector2((float)i / count, 0f));
                verts.Add(t0); norms.Add(normal); uvs.Add(new Vector2((float)i / count, 1f));
                verts.Add(t1); norms.Add(normal); uvs.Add(new Vector2((float)(i + 1) / count, 1f));
                verts.Add(b1); norms.Add(normal); uvs.Add(new Vector2((float)(i + 1) / count, 0f));

                // Front face
                tris.Add(vStart + 0); tris.Add(vStart + 1); tris.Add(vStart + 2);
                tris.Add(vStart + 0); tris.Add(vStart + 2); tris.Add(vStart + 3);

                // Back face
                tris.Add(vStart + 0); tris.Add(vStart + 2); tris.Add(vStart + 1);
                tris.Add(vStart + 0); tris.Add(vStart + 3); tris.Add(vStart + 2);
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Generates a planar floor ribbon mesh connecting left and right path boundaries.
        /// </summary>
        public static Mesh CreateTrackFloorRibbon(Vector3[] leftPath, Vector3[] rightPath)
        {
            Mesh mesh = new Mesh();
            mesh.name = "TrackFloorRibbon";

            if (leftPath == null || rightPath == null || leftPath.Length < 2 || rightPath.Length < 2) return mesh;

            int count = Mathf.Min(leftPath.Length, rightPath.Length);

            List<Vector3> verts = new List<Vector3>();
            List<Vector3> norms = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            Vector3 upNormal = Vector3.forward;

            for (int i = 0; i < count; i++)
            {
                verts.Add(leftPath[i]);
                norms.Add(upNormal);
                uvs.Add(new Vector2(0f, (float)i / (count - 1)));

                verts.Add(rightPath[i]);
                norms.Add(upNormal);
                uvs.Add(new Vector2(1f, (float)i / (count - 1)));
            }

            for (int i = 0; i < count - 1; i++)
            {
                int i0 = i * 2;
                int i1 = i * 2 + 1;
                int i2 = (i + 1) * 2 + 1;
                int i3 = (i + 1) * 2;

                tris.Add(i0); tris.Add(i1); tris.Add(i2);
                tris.Add(i0); tris.Add(i2); tris.Add(i3);

                // Double sided
                tris.Add(i0); tris.Add(i2); tris.Add(i1);
                tris.Add(i0); tris.Add(i3); tris.Add(i2);
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
