using System;
using UnityEngine;

namespace LlamAcademy.Dinos.Map
{
    public static class QuadrilateralMeshFactory
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        public static Mesh CreateGroundMesh(QuadrilateralXZ geometry, float y, string name)
        {
            Mesh mesh = CreateMesh(name, CreateLayerVertices(geometry, y));
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f)
            };
            mesh.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh CreateOutlineMesh(QuadrilateralXZ geometry, float y, string name)
        {
            Mesh mesh = CreateMesh(name, CreateLayerVertices(geometry, y));
            mesh.SetIndices(new[] { 0, 1, 1, 2, 2, 3, 3, 0 }, MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh CreateTriggerPrism(
            QuadrilateralXZ geometry,
            float bottomY,
            float topY,
            string name)
        {
            if (topY <= bottomY)
            {
                throw new ArgumentOutOfRangeException(nameof(topY), "Trigger top must be above its bottom.");
            }

            Vector3[] bottom = CreateLayerVertices(geometry, bottomY);
            Vector3[] top = CreateLayerVertices(geometry, topY);
            Vector3[] vertices =
            {
                bottom[0], bottom[1], bottom[2], bottom[3],
                top[0], top[1], top[2], top[3]
            };
            int[] triangles =
            {
                0, 1, 2, 0, 2, 3,
                4, 6, 5, 4, 7, 6,
                0, 4, 5, 0, 5, 1,
                1, 5, 6, 1, 6, 2,
                2, 6, 7, 2, 7, 3,
                3, 7, 4, 3, 4, 0
            };

            Mesh mesh = CreateMesh(name, vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static string ComputeSignature(Mesh mesh)
        {
            if (mesh == null)
            {
                return string.Empty;
            }

            ulong hash = FnvOffsetBasis;
            Vector3[] vertices = mesh.vertices;
            AddInt(ref hash, vertices.Length);
            for (int i = 0; i < vertices.Length; i++)
            {
                AddFloat(ref hash, vertices[i].x);
                AddFloat(ref hash, vertices[i].y);
                AddFloat(ref hash, vertices[i].z);
            }

            AddInt(ref hash, mesh.subMeshCount);
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                AddInt(ref hash, (int)mesh.GetTopology(subMesh));
                int[] indices = mesh.GetIndices(subMesh);
                AddInt(ref hash, indices.Length);
                for (int i = 0; i < indices.Length; i++)
                {
                    AddInt(ref hash, indices[i]);
                }
            }

            return hash.ToString("X16");
        }

        private static Mesh CreateMesh(string name, Vector3[] vertices)
        {
            Mesh mesh = new()
            {
                name = string.IsNullOrWhiteSpace(name) ? "Quadrilateral Mesh" : name,
                vertices = vertices
            };
            return mesh;
        }

        private static Vector3[] CreateLayerVertices(QuadrilateralXZ geometry, float y)
        {
            return new[]
            {
                new Vector3(geometry.A.x, y, geometry.A.y),
                new Vector3(geometry.B.x, y, geometry.B.y),
                new Vector3(geometry.C.x, y, geometry.C.y),
                new Vector3(geometry.D.x, y, geometry.D.y)
            };
        }

        private static void AddFloat(ref ulong hash, float value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= FnvPrime;
            }
        }

        private static void AddInt(ref ulong hash, int value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= FnvPrime;
            }
        }
    }
}
