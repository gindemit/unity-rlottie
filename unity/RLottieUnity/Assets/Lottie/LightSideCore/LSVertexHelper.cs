using System;
using System.Collections.Generic;
using UnityEngine.Pool;

namespace UnityEngine.UI
{
    public class LSVertexHelper : IDisposable
    {
        public List<Vector3> positions;
        private List<Color32> colors;
        private List<Vector4> uvs0;
        private List<Vector4> uvs1;
        private List<Vector4> uvs2;
        private List<Vector4> uvs3;
        private List<Vector3> normals;
        private List<Vector4> tangents;
        private List<int> indices;

        private static readonly Vector4 defaultTangent = new Vector4(1.0f, 0.0f, 0.0f, -1.0f);
        private static readonly Vector4 defaultUv = Vector4.zero;
        private static readonly Vector3 defaultNormal = Vector3.back;

        private bool listsInitalized = false;

        public LSVertexHelper()
        {}

        public LSVertexHelper(Mesh m)
        {
            

            positions.AddRange(m.vertices);
            colors.AddRange(m.colors32);
            List<Vector4> tempUVList = new List<Vector4>();
            m.GetUVs(0, tempUVList);
            uvs0.AddRange(tempUVList);
            m.GetUVs(1, tempUVList);
            uvs1.AddRange(tempUVList);
            m.GetUVs(2, tempUVList);
            uvs2.AddRange(tempUVList);
            m.GetUVs(3, tempUVList);
            uvs3.AddRange(tempUVList);
            normals.AddRange(m.normals);
            tangents.AddRange(m.tangents);
            indices.AddRange(m.GetIndices(0));
        }

        public void FillLegacy(VertexHelper vh)
        {
            vh.Clear();
            var vertCount = currentVertCount;
            for (int i = 0; i < vertCount; i++)
            {
                vh.AddVert(positions[i], colors[i], uvs0[i], uvs1[i], uvs2[i], uvs3[i], normals[i], tangents[i]);
            }

            var indexCount = currentIndexCount;
            for (int i = 0; i < indexCount; i += 3)
            {
                vh.AddTriangle(indices[i], indices[i+1], indices[i+2]);
            }
        }

        public void Init()
        {
            positions = ListPool<Vector3>.Get();
            colors = ListPool<Color32>.Get();
            uvs0 = ListPool<Vector4>.Get();
            uvs1 = ListPool<Vector4>.Get();
            uvs2 = ListPool<Vector4>.Get();
            uvs3 = ListPool<Vector4>.Get();
            normals = ListPool<Vector3>.Get();
            tangents = ListPool<Vector4>.Get();
            indices = ListPool<int>.Get();
            listsInitalized = true;
        }

        /// <summary>
        /// Cleanup allocated memory.
        /// </summary>
        public void Dispose()
        {
            if (listsInitalized)
            {
                ListPool<Vector3>.Release(positions);
                ListPool<Color32>.Release(colors);
                ListPool<Vector4>.Release(uvs0);
                ListPool<Vector4>.Release(uvs1);
                ListPool<Vector4>.Release(uvs2);
                ListPool<Vector4>.Release(uvs3);
                ListPool<Vector3>.Release(normals);
                ListPool<Vector4>.Release(tangents);
                ListPool<int>.Release(indices);

                positions = null;
                colors = null;
                uvs0 = null;
                uvs1 = null;
                uvs2 = null;
                uvs3 = null;
                normals = null;
                tangents = null;
                indices = null;

                listsInitalized = false;
            }
        }

        /// <summary>
        /// Clear all vertices from the stream.
        /// </summary>
        public void Clear()
        {
            // Only clear if we have our lists created.
            if (listsInitalized)
            {
                positions.Clear();
                colors.Clear();
                uvs0.Clear();
                uvs1.Clear();
                uvs2.Clear();
                uvs3.Clear();
                normals.Clear();
                tangents.Clear();
                indices.Clear();
            }
        }

        /// <summary>
        /// Current number of vertices in the buffer.
        /// </summary>
        public int currentVertCount
        {
            get { return positions != null ? positions.Count : 0; }
        }

        /// <summary>
        /// Get the number of indices set on the VertexHelper.
        /// </summary>
        public int currentIndexCount
        {
            get { return indices != null ? indices.Count : 0; }
        }

        /// <summary>
        /// Fill a UIVertex with data from index i of the stream.
        /// </summary>
        /// <param name="vertex">Vertex to populate</param>
        /// <param name="i">Index to populate.</param>
        public void PopulateUIVertex(ref UIVertex vertex, int i)
        {
            vertex.position = positions[i];
            vertex.color = colors[i];
            vertex.uv0 = uvs0[i];
            vertex.uv1 = uvs1[i];
            vertex.uv2 = uvs2[i];
            vertex.uv3 = uvs3[i];
            vertex.normal = normals[i];
            vertex.tangent = tangents[i];
        }
        
        public void PopulatePosition(ref Vector3 position, int i)
        {
            position = positions[i];
        }
        
        public void GetIndexes(int i, out int i0, out int i1, out int i2)
        {
            i0 = indices[i];
            i1 = indices[i + 1];
            i2 = indices[i + 2];
        }

        /// <summary>
        /// Set a UIVertex at the given index.
        /// </summary>
        /// <param name="vertex">The vertex to fill</param>
        /// <param name="i">the position in the current list to fill.</param>
        public void SetUIVertex(in UIVertex vertex, int i)
        {
            

            positions[i] = vertex.position;
            colors[i] = vertex.color;
            uvs0[i] = vertex.uv0;
            uvs1[i] = vertex.uv1;
            uvs2[i] = vertex.uv2;
            uvs3[i] = vertex.uv3;
            normals[i] = vertex.normal;
            tangents[i] = vertex.tangent;
        }

        /// <summary>
        /// Fill the given mesh with the stream data.
        /// </summary>
        public void FillMesh(Mesh mesh)
        {
            mesh.Clear();

            if (positions.Count >= 65000)
                throw new ArgumentException("Mesh can not have more than 65000 vertices");

            mesh.SetVertices(positions);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs0);
            mesh.SetUVs(1, uvs1);
            mesh.SetUVs(2, uvs2);
            mesh.SetUVs(3, uvs3);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
        }
        
        /// <summary>
        /// Fill the given mesh with the stream data.
        /// </summary>
        public void FillMeshUI(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(positions);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uvs0);
            mesh.SetTriangles(indices, 0);
        }

        /// <summary>
        /// Add a single vertex to the stream.
        /// </summary>
        /// <param name="position">Position of the vert</param>
        /// <param name="color">Color of the vert</param>
        /// <param name="uv0">UV of the vert</param>
        /// <param name="uv1">UV1 of the vert</param>
        /// <param name="uv2">UV2 of the vert</param>
        /// <param name="uv3">UV3 of the vert</param>
        /// <param name="normal">Normal of the vert.</param>
        /// <param name="tangent">Tangent of the vert</param>
        public void AddVert(in Vector3 position, in Color32 color, in Vector4 uv0, in Vector4 uv1, in Vector4 uv2, in Vector4 uv3, in Vector3 normal, in Vector4 tangent)
        {
            positions.Add(position);
            colors.Add(color);
            uvs0.Add(uv0);
            uvs1.Add(uv1);
            uvs2.Add(uv2);
            uvs3.Add(uv3);
            normals.Add(normal);
            tangents.Add(tangent);
        }
        
        public void AddVert(in Vector3 position, in Color32 color, in Vector4 uv0, in Vector4 uv1, in Vector4 uv2, in Vector4 uv3)
        {
            

            positions.Add(position);
            colors.Add(color);
            uvs0.Add(uv0);
            uvs1.Add(uv1);
            uvs2.Add(uv2);
            uvs3.Add(uv3);
            normals.Add(defaultNormal);
            tangents.Add(defaultTangent);
        }

        /// <summary>
        /// Add a single vertex to the stream.
        /// </summary>
        /// <param name="position">Position of the vert</param>
        /// <param name="color">Color of the vert</param>
        /// <param name="uv0">UV of the vert</param>
        /// <param name="uv1">UV1 of the vert</param>
        /// <param name="normal">Normal of the vert.</param>
        /// <param name="tangent">Tangent of the vert</param>
        public void AddVert(in Vector3 position, in Color32 color, in Vector4 uv0, in Vector4 uv1, in Vector3 normal, in Vector4 tangent)
        {
            
            
            positions.Add(position);
            colors.Add(color);
            uvs0.Add(uv0);
            uvs1.Add(uv1);
            uvs2.Add(defaultUv);
            uvs3.Add(defaultUv);
            normals.Add(normal);
            tangents.Add(tangent);
        }

        /// <summary>
        /// Add a single vertex to the stream.
        /// </summary>
        /// <param name="position">Position of the vert</param>
        /// <param name="color">Color of the vert</param>
        /// <param name="uv0">UV of the vert</param>
        public void AddVert(in Vector3 position, in Color32 color, in Vector4 uv0)
        {
            
            
            positions.Add(position);
            colors.Add(color);
            uvs0.Add(uv0);
            uvs1.Add(defaultUv);
            uvs2.Add(defaultUv);
            uvs3.Add(defaultUv);
            normals.Add(defaultNormal);
            tangents.Add(defaultTangent);
        }

        /// <summary>
        /// Add a single vertex to the stream.
        /// </summary>
        /// <param name="v">The vertex to add</param>
        public void AddVert(in UIVertex v)
        {
            
            
            positions.Add(v.position);
            colors.Add(v.color);
            uvs0.Add(v.uv0);
            uvs1.Add(v.uv1);
            uvs2.Add(v.uv2);
            uvs3.Add(v.uv3);
            normals.Add(v.normal);
            tangents.Add(v.tangent);
        }

        /// <summary>
        /// Add a triangle to the buffer.
        /// </summary>
        /// <param name="idx0">index 0</param>
        /// <param name="idx1">index 1</param>
        /// <param name="idx2">index 2</param>
        public void AddTriangle(int idx0, int idx1, int idx2)
        {
            

            indices.Add(idx0);
            indices.Add(idx1);
            indices.Add(idx2);
        }
        
        public void ClearTriangles()
        {
            
            indices.Clear();
        }

        /// <summary>
        /// Add a quad to the stream.
        /// </summary>
        /// <param name="verts">4 Vertices representing the quad.</param>
        public void AddUIVertexQuad(UIVertex[] verts)
        {
            int startIndex = currentVertCount;

            for (int i = 0; i < 4; i++)
            {
                
                ref var vert = ref verts[i];
                positions.Add(vert.position);
                colors.Add(vert.color);
                uvs0.Add(vert.uv0);
                uvs1.Add(vert.uv1);
                uvs2.Add(vert.uv2);
                uvs3.Add(vert.uv3);
                normals.Add(vert.normal);
                tangents.Add(vert.tangent);
            }

            AddTriangle(startIndex, startIndex + 1, startIndex + 2);
            AddTriangle(startIndex + 2, startIndex + 3, startIndex);
        }

        /// <summary>
        /// Add a stream of custom UIVertex and corresponding indices.
        /// </summary>
        /// <param name="verts">The custom stream of verts to add to the helpers internal data.</param>
        /// <param name="indices">The custom stream of indices to add to the helpers internal data.</param>
        public void AddUIVertexStream(List<UIVertex> verts, List<int> indices)
        {
            

            if (verts != null)
            {
                CanvasRenderer.AddUIVertexStream(verts, positions, colors, uvs0, uvs1, uvs2, uvs3, normals, tangents);
            }

            if (indices != null)
            {
                indices.AddRange(indices);
            }
        }

        /// <summary>
        /// Add a list of triangles to the stream.
        /// </summary>
        /// <param name="verts">Vertices to add. Length should be divisible by 3.</param>
        public void AddUIVertexTriangleStream(List<UIVertex> verts)
        {
            if (verts == null)
                return;

            

            CanvasRenderer.SplitUIVertexStreams(verts, positions, colors, uvs0, uvs1, uvs2, uvs3, normals, tangents, indices);
        }

        /// <summary>
        /// Create a stream of UI vertex (in triangles) from the stream.
        /// </summary>
        public void GetUIVertexStream(List<UIVertex> stream)
        {
            if (stream == null)
                return;

            

            CanvasRenderer.CreateUIVertexStream(stream, positions, colors, uvs0, uvs1, uvs2, uvs3, normals, tangents, indices);
        }
    }
}
