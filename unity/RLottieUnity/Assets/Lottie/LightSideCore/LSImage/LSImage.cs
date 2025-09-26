using System;
using System.Collections;
using LSCore.Extensions;
using UnityEngine;
using UnityEngine.UI;

namespace LSCore
{
    public partial class LSImage : Image
    {
        public enum RotationMode
        {
            None = 0,
            D90 = 1,
            D180 = 2,
            D270 = 3,
        }
        
        private static readonly LSVertexHelper vertexHelper = new LSVertexHelper();
        private RectTransform rt;
        private Rect currentRect;
        private bool isShowed;
        public event Action Showed;
        public event Action Hidden;

        static LSImage()
        {
            vertexHelper.Init();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            rt = rectTransform;
            base.OnValidate();
        }

        protected override void Reset()
        {
            rt = rectTransform;
            base.Reset();
        }
#endif

        protected override void OnEnable()
        {
            rt = rectTransform;
            base.OnEnable();
            StartCoroutine(DelayedOnEnable());
        }

        private IEnumerator DelayedOnEnable()
        {
            yield return null;

            if (!canvasRenderer.cull)
            {
                OnShowed();
            }

            onCullStateChanged.AddListener(OnCullStateChanged);
        }

        private void OnCullStateChanged(bool cull)
        {
            if (cull)
            {
                OnHidden();
            }
            else
            {
                OnShowed();
            }
        }

        protected override void OnDisable()
        {
            onCullStateChanged.RemoveListener(OnCullStateChanged);

            var cull = canvasRenderer.cull;

            base.OnDisable();

            if (!cull)
            {
                OnHidden();
            }
        }

        protected virtual void OnShowed()
        {
            if (isShowed) return;

            isShowed = true;
            Showed?.Invoke();
        }

        protected virtual void OnHidden()
        {
            if (!isShowed) return;

            isShowed = false;
            Hidden?.Invoke();
        }

        protected override void UpdateGeometry()
        {
            DoMeshGeneration();
        }

        private void DoMeshGeneration()
        {
            Action<Mesh> fillMesh = vertexHelper.FillMeshUI;
            vertexHelper.Clear();

            if (rt != null && rt.rect is { width: > 0, height: > 0 })
            {
                InitMeshPopulator();
                OnPopulateMesh(vertexHelper);
            }

            var mesh = workerMesh;
            fillMesh(mesh);
            canvasRenderer.SetMesh(mesh);
        }

        private Action<LSVertexHelper> meshPopulator;

        private void InitMeshPopulator()
        {
            if (overrideSprite == null)
            {
                meshPopulator = GenerateDefaultSprite;
                return;
            }

            switch (type)
            {
                case Type.Simple:
                    if (!useSpriteMesh)
                    {
                        meshPopulator = GenerateSimpleSprite;
                    }
                    else
                    {
                        meshPopulator = GenerateSprite;
                    }
                    break;
                case Type.Sliced:
                    meshPopulator = GenerateSlicedSprite;
                    break;
                case Type.Tiled:
                    meshPopulator = GenerateTiledSprite;
                    break;
                case Type.Filled:
                    if (combineFilledWithSliced && hasBorder && type == Type.Filled &&
                        fillMethod is FillMethod.Horizontal or FillMethod.Vertical)
                    {
                        meshPopulator = GenerateFilledSlicedSprite;
                    }
                    else
                    {
                        meshPopulator = GenerateFilledSprite;
                    }

                    break;
            }
        }

        private void OnPopulateMesh(LSVertexHelper toFill)
        {
            defaultColor = color;
            meshPopulator(toFill);
            PostProcessMesh(toFill);
        }
        
        private void PostProcessMesh(LSVertexHelper vh)
        {
            if (meshPopulator != GenerateFilledSlicedSprite)
            {
                Mirror(vh);
            }
            
            RotateMesh(vh);
        }

        private void Mirror(LSVertexHelper vh)
        {
            if (mirror.x == 1)
            {
                Mirror(vh, true);
            }
            
            if (mirror.y == 1)
            {
                Mirror(vh, false);
            }
        }
        
        private void Mirror(LSVertexHelper vh, bool reflectHorizontally)
        {
            Vector3 tempPos = default;
            var count = vh.currentVertCount;
            if (count == 0) return;
            
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            
            for (int i = 0; i < count; i++)
            {
                vh.PopulatePosition(ref tempPos, i);
                
                if (tempPos.x > maxX) maxX = tempPos.x;
                if (tempPos.y > maxY) maxY = tempPos.y;
            }
            
            UIVertex reflectedVertex = default;
            
            for (int i = 0; i < count; i++)
            {
                vh.PopulateUIVertex(ref reflectedVertex, i);
                var pos = reflectedVertex.position;
                
                if (reflectHorizontally)
                {
                    float offsetX = maxX - pos.x;
                    pos.x = maxX + offsetX;
                }
                else
                {
                    float offsetY = maxY - pos.y;
                    pos.y = maxY + offsetY;
                }
                
                reflectedVertex.position = pos;
                vh.AddVert(reflectedVertex);
            }
            
            var currentTriangleCount = vh.currentIndexCount;
            for (int i = 0; i < currentTriangleCount; i += 3)
            {
                vh.GetIndexes(i, out int i0, out int i1, out int i2);
                vh.AddTriangle(i0 + count, i1 + count, i2 + count);
            }
        }

        public delegate void RotateAction(ref Vector3 value, in Vector2 center);
        private RotateAction rotateAction;

        private void RotateMesh(LSVertexHelper vh)
        {
            if(rotateId == 0 && flip is { x: 0, y: 0 }) return;
            
            UIVertex vert = new UIVertex();
            var count = vh.currentVertCount;
            var center = rt.rect.center * 2;

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
        
        private void Invert(ref Vector2 pos, in Vector2 center, bool flipX, bool flipY)
        {
            float xOffset = pos.x - center.x;
            float yOffset = pos.y - center.y;
            
            if (flipX)
            {
                pos.x = -xOffset;
            }

            if (flipY)
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
        
        private void TryRotateRect(ref Rect rect)
        {
            if (rotateId % 2 == 1)
            {
                (rect.width, rect.height) = (rect.height, rect.width);
                var pos = rect.position;
                (pos.x, pos.y) = (pos.y, pos.x);
                rect.position = pos;
            }
            
            if (mirror.x == 1)
            {
                rect = rect.Split(0, 2);
            }
            
            if (mirror.y == 1)
            {
                rect = rect.SplitVertical(0, 2);
            }
        }
    }
}