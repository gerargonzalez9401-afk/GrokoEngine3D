using System;
using MiMotor.Mathematics;

namespace GrokoEngine
{
    // Genera un ParsedMesh procedural (grilla de quads) a partir del heightmap de un Terrain.
    // Desde la reconstrucción CDLOD del render (que desplaza una rejilla en GPU y ya no usa esta
    // malla), este generador queda SOLO para la colisión estática de Bepu.
    // 'stride' > 1 decima la malla (muestrea 1 de cada N celdas): a resoluciones altas la
    // colisión no necesita el detalle completo y una malla de millones de triángulos tarda
    // segundos en construirse en Bepu.
    public static class TerrainMeshGenerator
    {
        public static ParsedMesh Generate(Terrain terrain, Terrain? left = null, Terrain? right = null, Terrain? top = null, Terrain? bottom = null, int stride = 1)
        {
            int fullRes = Math.Max(2, terrain.Resolution);
            terrain.EnsureHeightmapSize();

            stride = Math.Max(1, stride);
            if (stride > 1)
                return GenerateDecimated(terrain, fullRes, stride);

            int res = fullRes;

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

            // Normales por diferencias finitas con vecinos. Las alturas vecinas ya se calcularon
            // (y quedaron escaladas por HeightScale) en el paso de posiciones de arriba, así que
            // se leen directo de 'positions' en vez de volver a llamar Terrain.GetHeight — evita
            // ~4 llamadas redundantes (clamp + indexado) por vértice, que en un terreno de
            // resolución alta (256x256 = 65536 vértices) suman cientos de miles de llamadas de más
            // en cada regeneración de malla (cada trazo de sculpt en un terreno sin cachear).
            // En el borde, si hay un vecino de la MISMA Resolution conectado, se lee su altura
            // real más allá del borde en vez de clampear contra la propia (que duplicaba el
            // valor del borde y producía una costura de iluminación visible entre dos Terrain
            // pegados, aunque las alturas coincidieran exactamente en la unión).
            float HeightAt(int hx, int hz)
            {
                if (hx < 0 && left != null && left.Resolution == res)
                    return left.GetHeight(left.Resolution - 1, ClampIndex(hz, res)) * left.HeightScale;
                if (hx >= res && right != null && right.Resolution == res)
                    return right.GetHeight(0, ClampIndex(hz, res)) * right.HeightScale;
                if (hz < 0 && bottom != null && bottom.Resolution == res)
                    return bottom.GetHeight(ClampIndex(hx, res), bottom.Resolution - 1) * bottom.HeightScale;
                if (hz >= res && top != null && top.Resolution == res)
                    return top.GetHeight(ClampIndex(hx, res), 0) * top.HeightScale;

                hx = hx < 0 ? 0 : hx >= res ? res - 1 : hx;
                hz = hz < 0 ? 0 : hz >= res ? res - 1 : hz;
                return positions[(hz * res + hx) * 3 + 1];
            }

            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    int i = z * res + x;

                    float hL = HeightAt(x - 1, z);
                    float hR = HeightAt(x + 1, z);
                    float hD = HeightAt(x, z - 1);
                    float hU = HeightAt(x, z + 1);

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

        // Malla decimada para colisión: rejilla de ((fullRes-1)/stride)+1 muestras que SIEMPRE
        // incluye la fila/columna del borde (clampeando el índice), para que la colisión cubra
        // exactamente el mismo rectángulo SizeX×SizeZ que el render.
        private static ParsedMesh GenerateDecimated(Terrain terrain, int fullRes, int stride)
        {
            int quads = Math.Max(1, (fullRes - 1 + stride - 1) / stride);
            int res = quads + 1;

            float halfX = terrain.SizeX * 0.5f;
            float halfZ = terrain.SizeZ * 0.5f;

            var positions = new float[res * res * 3];
            var normals = new float[res * res * 3];
            var uvs = new float[res * res * 2];

            for (int z = 0; z < res; z++)
            {
                int cellZ = Math.Min(z * stride, fullRes - 1);
                float pz = (float)cellZ / (fullRes - 1) * terrain.SizeZ - halfZ;
                for (int x = 0; x < res; x++)
                {
                    int cellX = Math.Min(x * stride, fullRes - 1);
                    int i = z * res + x;
                    positions[i * 3 + 0] = (float)cellX / (fullRes - 1) * terrain.SizeX - halfX;
                    positions[i * 3 + 1] = terrain.GetHeight(cellX, cellZ) * terrain.HeightScale;
                    positions[i * 3 + 2] = pz;
                    normals[i * 3 + 1] = 1f; // la colisión no usa normales de vértice
                    uvs[i * 2 + 0] = (float)cellX / (fullRes - 1);
                    uvs[i * 2 + 1] = (float)cellZ / (fullRes - 1);
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

        private static int ClampIndex(int v, int res) => v < 0 ? 0 : v >= res ? res - 1 : v;

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
