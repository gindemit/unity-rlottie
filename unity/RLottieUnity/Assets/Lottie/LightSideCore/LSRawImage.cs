using System;
using LSCore.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace LSCore
{
    public class LSRawImage : RawImage
    {
        private static readonly LSVertexHelper vertexHelper = new();
        
        static LSRawImage()
        {
            vertexHelper.Init();
        }
        
        public delegate void RotateAction(ref Vector3 value, in Vector2 center);
        private RotateAction rotateAction;
        
        [SerializeField] private bool preserveAspectRatio;
        [SerializeField] private int rotateId = 0;
        [SerializeField] private Vector2Int flip;
        
        public (bool x, bool y) Flip
        {
            get => (flip.x.ToBool(),  flip.y.ToBool());
            set
            {
                flip = new Vector2Int(value.x.ToInt(), value.y.ToInt());
                SetVerticesDirty();
            }
        }
        
        public LSImage.RotationMode Rotation
        {
            get => (LSImage.RotationMode)rotateId;
            set
            {
                rotateId = (int)value;
                SetVerticesDirty();
            }
        }
        
        public bool PreserveAspectRatio
        {
            get { return preserveAspectRatio; }
            set
            {
                preserveAspectRatio = value;
                SetVerticesDirty();
            }
        }
        
        protected override void UpdateGeometry()
        {
            DoMeshGeneration();
        }

        private void DoMeshGeneration()
        {
            Action<Mesh> fillMesh = vertexHelper.FillMeshUI;
            vertexHelper.Clear();

            if (rectTransform != null && rectTransform.rect is { width: > 0, height: > 0 })
            {
                OnPopulateMesh(vertexHelper);
            }

            var mesh = workerMesh;
            fillMesh(mesh);
            OnMeshFilled(mesh);
            canvasRenderer.SetMesh(mesh);
        }

        public Rect MeshRect
        {
            get
            {
                float texAspect = Aspect;
                Rect r = GetPixelAdjustedRect();
        
                Vector2 pivot = rectTransform.pivot;
        
                float newWidth, newHeight;
                float rAspect = r.width / r.height;
                if (rAspect > texAspect)
                {
                    newHeight = r.height;
                    newWidth = newHeight * texAspect;
                }
                else
                {
                    newWidth = r.width;
                    newHeight = newWidth / texAspect;
                }
        
                float offsetX = r.x + r.width * pivot.x - newWidth * pivot.x;
                float offsetY = r.y + r.height * pivot.y - newHeight * pivot.y;
        
                float xMin = offsetX;
                float yMin = offsetY;
                float xMax = offsetX + newWidth;
                float yMax = offsetY + newHeight;
                return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
            }
        }
        
        protected virtual float Aspect => mainTexture.AspectRatio();
        protected virtual void OnMeshFilled(Mesh mesh){}
        
        protected void OnPopulateMesh(LSVertexHelper vh)
        {
            Texture tex = mainTexture;
            vh.Clear();
            if(tex == null) return;
            
            if (preserveAspectRatio)
            {
                var meshRect = MeshRect;
                Color32 color32 = color;
                vh.AddVert(new Vector3(meshRect.xMin, meshRect.yMin), color32, new Vector2(0, 0));
                vh.AddVert(new Vector3(meshRect.xMin, meshRect.yMax), color32, new Vector2(0, 1));
                vh.AddVert(new Vector3(meshRect.xMax, meshRect.yMax), color32, new Vector2(1, 1));
                vh.AddVert(new Vector3(meshRect.xMax, meshRect.yMin), color32, new Vector2(1, 0));
        
                vh.AddTriangle(0, 1, 2);
                vh.AddTriangle(2, 3, 0);
            }
            else
            {
                var r = GetPixelAdjustedRect();
                var v = new Vector4(r.x, r.y, r.x + r.width, r.y + r.height);
                var scaleX = tex.width * tex.texelSize.x;
                var scaleY = tex.height * tex.texelSize.y;
                {
                    var color32 = color;
                    vh.AddVert(new Vector3(v.x, v.y), color32, new Vector2(0, 0));
                    vh.AddVert(new Vector3(v.x, v.w), color32, new Vector2(0, scaleY));
                    vh.AddVert(new Vector3(v.z, v.w), color32, new Vector2(scaleX, scaleY));
                    vh.AddVert(new Vector3(v.z, v.y), color32, new Vector2(scaleX, 0));

                    vh.AddTriangle(0, 1, 2);
                    vh.AddTriangle(2, 3, 0);
                }
            }
            
            PostProcessMesh(vh);
        }
        
        private void PostProcessMesh(LSVertexHelper vh)
        {
            RotateMesh(vh);
        }
        
        private void RotateMesh(LSVertexHelper vh)
        {
            if(rotateId == 0 && flip is { x: 0, y: 0 }) return;
            
            UIVertex vert = new UIVertex();
            var count = vh.currentVertCount;
            var center = rectTransform.rect.center * 2;

            rotateAction = rotateId switch
            {
                1 => Rotate90,
                2 => Rotate180,
                3 => Rotate270,
                _ => null
            };
            
            rotateAction += Invert;
            
            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref vert, i);
                var pos = vert.position;
                rotateAction(ref pos, center);
                vert.position = pos;
                vh.SetUIVertex(vert, i);
            }

            count = vh.currentIndexCount / 6 * 4;
            vh.ClearTriangles();
            
            if (rotateId % 2 == 1)
            {
                for (int i = 0; i < count; i += 4)
                {
                    vh.AddTriangle(i+1, i + 2, i+3);
                    vh.AddTriangle(i+3, i, i+1);
                }
            }
            else
            {
                for (int i = 0; i < count; i += 4)
                {
                    vh.AddTriangle(i, i + 1, i + 2);
                    vh.AddTriangle(i + 2, i + 3, i);
                }
            }

        }
        
        private void Invert(ref Vector3 pos, in Vector2 center)
        {
            float xOffset = pos.x - center.x;
            float yOffset = pos.y - center.y;
            
            if (flip.x == 1)
            {
                pos.x = -xOffset;
            }

            if (flip.y == 1)
            {
                pos.y = -yOffset;
            }
        }
        
        private void Rotate90(ref Vector3 pos, in Vector2 center)
        {
            var x = pos.x;
            pos.x = -pos.y + center.x;
            pos.y = x;
        }
        
        private void Rotate180(ref Vector3 pos, in Vector2 center)
        {
            pos.x = -pos.x + center.x;
            pos.y = -pos.y + center.y;
        }
        
        private void Rotate270(ref Vector3 pos, in Vector2 center)
        {
            var x = pos.x;
            pos.x = pos.y;
            pos.y = -x + center.y;
        }
    }
}