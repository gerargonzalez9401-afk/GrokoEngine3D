using System;
using MiMotor.Mathematics;

namespace GrokoEngine
{
    // Genera un ParsedMesh procedural (grilla de quads) a partir del heightmap de un Terrain,
    // en el mismo formato que usa ObjLoader para mallas importadas.
    public static class TerrainMeshGenerator
    {
        public static ParsedMesh Generate(Terrain terrain)
        {
            int res = Math.Max(2, terrain.Resolution);
            terrain.EnsureHeightmapSize();

            int vertexCount = res * res;
            var positions = new float[vertexCount * 3];
            var normals = new float[vertexCount * 3];
            var uvs = new float[vertexCount * 2];

            float halfX = terrain.SizeX * 0.5f;
            float halfZ = terrain.SizeZ * 0.5f;
            float stepX = res > 1 ? terrain.SizeX / (res - 1) : 0f;
            float stepZ = res > 1 ? terrain.SizeZ / (res - 1) : 0f;

            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i = z * res + x;
                    float px = x * stepX - halfX;
                    float pz = z * stepZ - halfZ;
                    float py = terrain.GetHeight(x, z) * terrain.HeightScale;

                    positions[i * 3 + 0] = px;
                    positions[i * 3 + 1] = py;
                    positions[i * 3 + 2] = pz;

                    uvs[i * 2 + 0] = res > 1 ? (float)x / (res - 1) : 0f;
                    uvs[i * 2 + 1] = res > 1 ? (float)z / (res - 1) : 0f;
                }
            }

            // Normales por diferencias finitas con vecinos (cae bien al borde gracias al clamp de GetHeight)
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i = z * res + x;

                    float hL = terrain.GetHeight(x - 1, z) * terrain.HeightScale;
                    float hR = terrain.GetHeight(x + 1, z) * terrain.HeightScale;
                    float hD = terrain.GetHeight(x, z - 1) * terrain.HeightScale;
                    float hU = terrain.GetHeight(x, z + 1) * terrain.HeightScale;

                    var tangentX = new Vector3(2f * stepX, hR - hL, 0f);
                    var tangentZ = new Vector3(0f, hU - hD, 2f * stepZ);

                    var normal = Vector3.Cross(tangentZ, tangentX).Normalized();

                    normals[i * 3 + 0] = normal.X;
                    normals[i * 3 + 1] = normal.Y;
                    normals[i * 3 + 2] = normal.Z;
                }
            }

            int quadCount = (res - 1) * (res - 1);
            int triangleCount = quadCount * 2;
            var trianglePositions = new float[triangleCount * 3 * 3];
            var triangleNormals = new float[triangleCount * 3 * 3];
            var triangleUvs = new float[triangleCount * 3 * 2];

            int t = 0;
            for (int z = 0; z < res - 1; z++)
            {
                for (int x = 0; x < res - 1; x++)
                {
                    int i00 = z * res + x;
                    int i10 = z * res + (x + 1);
                    int i01 = (z + 1) * res + x;
                    int i11 = (z + 1) * res + (x + 1);

                    WriteTriangle(trianglePositions, triangleNormals, triangleUvs, ref t, positions, normals, uvs, i00, i11, i10);
                    WriteTriangle(trianglePositions, triangleNormals, triangleUvs, ref t, positions, normals, uvs, i00, i01, i11);
                }
            }

            var mesh = new ParsedMesh
            {
                Positions = trianglePositions,
                Normals = triangleNormals,
                UVs = triangleUvs,
                TriangleCount = triangleCount,
                BoundsMin = new Vector3(-halfX, 0f, -halfZ),
                BoundsMax = new Vector3(halfX, terrain.HeightScale, halfZ)
            };

            mesh.Submeshes.Add(new MeshSubmesh
            {
                Name = "Terrain",
                VertexStart = 0,
                VertexCount = triangleCount * 3,
                DiffuseR = 0.45f,
                DiffuseG = 0.55f,
                DiffuseB = 0.35f
            });

            return mesh;
        }

        private static void WriteTriangle(float[] outPositions, float[] outNormals, float[] outUvs, ref int triIndex,
            float[] positions, float[] normals, float[] uvs, int a, int b, int c)
        {
            WriteVertex(outPositions, outNormals, outUvs, triIndex * 3 + 0, positions, normals, uvs, a);
            WriteVertex(outPositions, outNormals, outUvs, triIndex * 3 + 1, positions, normals, uvs, b);
            WriteVertex(outPositions, outNormals, outUvs, triIndex * 3 + 2, positions, normals, uvs, c);
            triIndex++;
        }

        private static void WriteVertex(float[] outPositions, float[] outNormals, float[] outUvs, int outIndex,
            float[] positions, float[] normals, float[] uvs, int srcIndex)
        {
            outPositions[outIndex * 3 + 0] = positions[srcIndex * 3 + 0];
            outPositions[outIndex * 3 + 1] = positions[srcIndex * 3 + 1];
            outPositions[outIndex * 3 + 2] = positions[srcIndex * 3 + 2];

            outNormals[outIndex * 3 + 0] = normals[srcIndex * 3 + 0];
            outNormals[outIndex * 3 + 1] = normals[srcIndex * 3 + 1];
            outNormals[outIndex * 3 + 2] = normals[srcIndex * 3 + 2];

            outUvs[outIndex * 2 + 0] = uvs[srcIndex * 2 + 0];
            outUvs[outIndex * 2 + 1] = uvs[srcIndex * 2 + 1];
        }
    }
}
