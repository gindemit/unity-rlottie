using System;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;
using static UnityEngine.Mathf;

namespace LSCore
{
    public partial class LSImage
    {
        private void GenerateDefaultSprite(LSVertexHelper vh)
        {
            var rect = GetPixelAdjustedRect();
            TryRotateRect(ref rect);
            currentRect = rect;
            var v = new Vector4(rect.x, rect.y, rect.x + rect.width, rect.y + rect.height);

            AddVert(vh, new Vector3(v.x, v.y), new Vector4(0f, 0f));
            AddVert(vh, new Vector3(v.x, v.w), new Vector4(0f, 1f));
            AddVert(vh, new Vector3(v.z, v.w), new Vector4(1f, 1f));
            AddVert(vh, new Vector3(v.z, v.y), new Vector4(1f, 0f));

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }


        //-------------GENERATORS-------------
        private void GenerateSlicedSprite(LSVertexHelper toFill)
        {
            SlicedPrepare(toFill);

            var posMin = default(Vector2);
            var posMax = default(Vector2);
            
            var uvMin = default(Vector2);
            var uvMax = default(Vector2);
            
            
            for (int x = 0; x < 3; ++x)
            {
                int x2 = x + 1;

                for (int y = 0; y < 3; ++y)
                {
                    if (!fillCenter && x == 1 && y == 1)
                        continue;

                    int y2 = y + 1;
                    posMin.x = vertScratch[x].x; posMin.y = vertScratch[y].y;
                    posMax.x = vertScratch[x2].x; posMax.y = vertScratch[y2].y;
                    
                    uvMin.x = uVScratch[x].x; uvMin.y = uVScratch[y].y;
                    uvMax.x = uVScratch[x2].x; uvMax.y = uVScratch[y2].y;
                    
                    AddQuad(toFill, posMin, posMax, uvMin, uvMax);
                }
            }
        }
        
        private void SlicedPrepare(LSVertexHelper toFill)
        {
            if (!hasBorder)
            {
                GenerateSimpleSprite(toFill, false);
                return;
            }

            var activeSprite = overrideSprite;
            Vector4 outer, inner, padding, border;

            if (activeSprite != null)
            {
                outer = DataUtility.GetOuterUV(activeSprite);
                inner = DataUtility.GetInnerUV(activeSprite);
                padding = DataUtility.GetPadding(activeSprite);
                border = activeSprite.border;
            }
            else
            {
                outer = Vector4.zero;
                inner = Vector4.zero;
                padding = Vector4.zero;
                border = Vector4.zero;
            }

            Rect rect = GetPixelAdjustedRect();
            var lastCenter = rect.center * 2;
            TryRotateRect(ref rect);
            currentRect = rect;
            var pixelsPerUnit = multipliedPixelsPerUnit;
            Vector4 adjustedBorders = GetAdjustedBorders(border / pixelsPerUnit, rect);
            padding /= pixelsPerUnit;


            vertScratch[0] = new Vector2(padding.x, padding.y);
            vertScratch[3] = new Vector2(rect.width - padding.z, rect.height - padding.w);

            vertScratch[1].x = adjustedBorders.x;
            vertScratch[1].y = adjustedBorders.y;

            vertScratch[2].x = rect.width - adjustedBorders.z;
            vertScratch[2].y = rect.height - adjustedBorders.w;

            for (int i = 0; i < 4; ++i)
            {
                vertScratch[i].x += rect.x;
                vertScratch[i].y += rect.y;
            }

            uVScratch[0] = new Vector2(outer.x, outer.y);
            uVScratch[1] = new Vector2(inner.x, inner.y);
            uVScratch[2] = new Vector2(inner.z, inner.w);
            uVScratch[3] = new Vector2(outer.z, outer.w);
        }
        
        public new Rect GetPixelAdjustedRect()
        {
            var canv = canvas;
            
            if (!canv || canv.renderMode == RenderMode.WorldSpace || canv.scaleFactor == 0.0f || !canv.pixelPerfect)
            {
                return rectTransform.rect;
            }
            
            return RectTransformUtility.PixelAdjustRect(rectTransform, canv);
        }
        
        private void GenerateSimpleSprite(LSVertexHelper toFill) => GenerateSimpleSprite(toFill, preserveAspect);

        private void GenerateSimpleSprite(LSVertexHelper vh, bool lPreserveAspect)
        {
            var activeSprite = overrideSprite;
            Vector4 v = GetDrawingDimensions(lPreserveAspect);
            var uv = activeSprite != null ? DataUtility.GetOuterUV(activeSprite) : Vector4.zero;
            
            AddVert(vh, new Vector3(v.x, v.y), new Vector4(uv.x, uv.y));
            AddVert(vh, new Vector3(v.x, v.w), new Vector4(uv.x, uv.w));
            AddVert(vh, new Vector3(v.z, v.w), new Vector4(uv.z, uv.w));
            AddVert(vh, new Vector3(v.z, v.y), new Vector4(uv.z, uv.y));

            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
        }

        private void GenerateSprite(LSVertexHelper vh) => GenerateSprite(vh, preserveAspect);
        private void GenerateSprite(LSVertexHelper vh, bool lPreserveAspect)
        {
            var activeSprite = overrideSprite;
            var spriteSize = new Vector2(activeSprite.rect.width, activeSprite.rect.height);
            
            // Covert sprite pivot into normalized space.
            var spritePivot = activeSprite.pivot / spriteSize;
            var rectPivot = rt.pivot;
            Rect rect = GetPixelAdjustedRect();
            
            if (lPreserveAspect & spriteSize.sqrMagnitude > 0.0f)
            {
                PreserveSpriteAspectRatio(ref rect, spriteSize);
            }
            
            TryRotateRect(ref rect);
            currentRect = rect;
            
            var drawingSize = new Vector2(rect.width, rect.height);
            var spriteBoundSize = activeSprite.bounds.size;

            // Calculate the drawing offset based on the difference between the two pivots.
            var drawOffset = (rectPivot - spritePivot) * drawingSize;

            Vector2[] vertices = activeSprite.vertices;
            Vector2[] uvs = activeSprite.uv;
            for (int i = 0; i < vertices.Length; ++i)
            {
                AddVert(vh, new Vector3(vertices[i].x / spriteBoundSize.x * drawingSize.x - drawOffset.x, vertices[i].y / spriteBoundSize.y * drawingSize.y - drawOffset.y), new Vector4(uvs[i].x, uvs[i].y));
            }

            UInt16[] triangles = activeSprite.triangles;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                vh.AddTriangle(triangles[i + 0], triangles[i + 1], triangles[i + 2]);
            }
        }

        private void GenerateTiledSprite(LSVertexHelper toFill)
        {
            Vector4 outer, inner, border;
            Vector2 spriteSize;
            var activeSprite = overrideSprite;

            if (activeSprite != null)
            {
                outer = DataUtility.GetOuterUV(activeSprite);
                inner = DataUtility.GetInnerUV(activeSprite);
                border = activeSprite.border;
                spriteSize = activeSprite.rect.size;
            }
            else
            {
                outer = Vector4.zero;
                inner = Vector4.zero;
                border = Vector4.zero;
                spriteSize = Vector2.one * 100;
            }

            Rect rect = GetPixelAdjustedRect();
            TryRotateRect(ref rect);
            currentRect = rect;
            float tileWidth = (spriteSize.x - border.x - border.z) / multipliedPixelsPerUnit;
            float tileHeight = (spriteSize.y - border.y - border.w) / multipliedPixelsPerUnit;
            
            border = GetAdjustedBorders(border / multipliedPixelsPerUnit, rect);
            

            
            var uvMin = new Vector2(inner.x, inner.y);
            var uvMax = new Vector2(inner.z, inner.w);

            // Min to max max range for tiled region in coordinates relative to lower left corner.
            float xMin = border.x;
            float xMax = rect.width - border.z;
            float yMin = border.y;
            float yMax = rect.height - border.w;
            
            var clipped = uvMax;

            // if either width is zero we cant tile so just assume it was the full width.
            if (tileWidth <= 0)
                tileWidth = xMax - xMin;

            if (tileHeight <= 0)
                tileHeight = yMax - yMin;

            if (activeSprite != null && (hasBorder || activeSprite.packed || activeSprite.texture != null && activeSprite.texture.wrapMode != TextureWrapMode.Repeat))
            {
                // Sprite has border, or is not in repeat mode, or cannot be repeated because of packing.
                // We cannot use texture tiling so we will generate a mesh of quads to tile the texture.

                // Evaluate how many vertices we will generate. Limit this number to something sane,
                // especially since meshes can not have more than 65000 vertices.

                long nTilesW = 0;
                long nTilesH = 0;
                if (fillCenter)
                {
                    nTilesW = (long)Math.Ceiling((xMax - xMin) / tileWidth);
                    nTilesH = (long)Math.Ceiling((yMax - yMin) / tileHeight);

                    double nVertices = 0;
                    if (hasBorder)
                    {
                        nVertices = (nTilesW + 2.0) * (nTilesH + 2.0) * 4.0; // 4 vertices per tile
                    }
                    else
                    {
                        nVertices = nTilesW * nTilesH * 4.0; // 4 vertices per tile
                    }

                    if (nVertices > 65000.0)
                    {
                        Debug.LogError("Too many sprite tiles on Image \"" + name + "\". The tile size will be increased. To remove the limit on the number of tiles, set the Wrap mode to Repeat in the Image Import Settings", this);

                        double maxTiles = 65000.0 / 4.0; // Max number of vertices is 65000; 4 vertices per tile.
                        double imageRatio;
                        if (hasBorder)
                        {
                            imageRatio = (nTilesW + 2.0) / (nTilesH + 2.0);
                        }
                        else
                        {
                            imageRatio = (double)nTilesW / nTilesH;
                        }

                        double targetTilesW = Math.Sqrt(maxTiles / imageRatio);
                        double targetTilesH = targetTilesW * imageRatio;
                        if (hasBorder)
                        {
                            targetTilesW -= 2;
                            targetTilesH -= 2;
                        }

                        nTilesW = (long)Math.Floor(targetTilesW);
                        nTilesH = (long)Math.Floor(targetTilesH);
                        tileWidth = (xMax - xMin) / nTilesW;
                        tileHeight = (yMax - yMin) / nTilesH;
                    }
                }
                else
                {
                    if (hasBorder)
                    {
                        // Texture on the border is repeated only in one direction.
                        nTilesW = (long)Math.Ceiling((xMax - xMin) / tileWidth);
                        nTilesH = (long)Math.Ceiling((yMax - yMin) / tileHeight);
                        double nVertices = (nTilesH + nTilesW + 2.0 /*corners*/) * 2.0 /*sides*/ * 4.0 /*vertices per tile*/;
                        if (nVertices > 65000.0)
                        {
                            Debug.LogError("Too many sprite tiles on Image \"" + name + "\". The tile size will be increased. To remove the limit on the number of tiles, set the Wrap mode to Repeat in the Image Import Settings", this);

                            double maxTiles = 65000.0 / 4.0; // Max number of vertices is 65000; 4 vertices per tile.
                            double imageRatio = (double)nTilesW / nTilesH;
                            double targetTilesW = (maxTiles - 4 /*corners*/) / (2 * (1.0 + imageRatio));
                            double targetTilesH = targetTilesW * imageRatio;

                            nTilesW = (long)Math.Floor(targetTilesW);
                            nTilesH = (long)Math.Floor(targetTilesH);
                            tileWidth = (xMax - xMin) / nTilesW;
                            tileHeight = (yMax - yMin) / nTilesH;
                        }
                    }
                    else
                    {
                        nTilesH = nTilesW = 0;
                    }
                }

                if (fillCenter)
                {
                    // TODO: we could share vertices between quads. If vertex sharing is implemented. update the computation for the number of vertices accordingly.
                    for (long j = 0; j < nTilesH; j++)
                    {
                        float y1 = yMin + j * tileHeight;
                        float y2 = yMin + (j + 1) * tileHeight;
                        if (y2 > yMax)
                        {
                            clipped.y = uvMin.y + (uvMax.y - uvMin.y) * (yMax - y1) / (y2 - y1);
                            y2 = yMax;
                        }
                        clipped.x = uvMax.x;
                        for (long i = 0; i < nTilesW; i++)
                        {
                            float x1 = xMin + i * tileWidth;
                            float x2 = xMin + (i + 1) * tileWidth;
                            if (x2 > xMax)
                            {
                                clipped.x = uvMin.x + (uvMax.x - uvMin.x) * (xMax - x1) / (x2 - x1);
                                x2 = xMax;
                            }
                            AddQuad(toFill, new Vector2(x1, y1) + rect.position, new Vector2(x2, y2) + rect.position,  uvMin, clipped);
                        }
                    }
                }
                if (hasBorder)
                {
                    clipped = uvMax;
                    for (long j = 0; j < nTilesH; j++)
                    {
                        float y1 = yMin + j * tileHeight;
                        float y2 = yMin + (j + 1) * tileHeight;
                        if (y2 > yMax)
                        {
                            clipped.y = uvMin.y + (uvMax.y - uvMin.y) * (yMax - y1) / (y2 - y1);
                            y2 = yMax;
                        }
                        AddQuad(toFill,
                            new Vector2(0, y1) + rect.position,
                            new Vector2(xMin, y2) + rect.position,
                            
                            new Vector2(outer.x, uvMin.y),
                            new Vector2(uvMin.x, clipped.y));
                        AddQuad(toFill,
                            new Vector2(xMax, y1) + rect.position,
                            new Vector2(rect.width, y2) + rect.position,
                            
                            new Vector2(uvMax.x, uvMin.y),
                            new Vector2(outer.z, clipped.y));
                    }

                    // Bottom and top tiled border
                    clipped = uvMax;
                    for (long i = 0; i < nTilesW; i++)
                    {
                        float x1 = xMin + i * tileWidth;
                        float x2 = xMin + (i + 1) * tileWidth;
                        if (x2 > xMax)
                        {
                            clipped.x = uvMin.x + (uvMax.x - uvMin.x) * (xMax - x1) / (x2 - x1);
                            x2 = xMax;
                        }
                        AddQuad(toFill,
                            new Vector2(x1, 0) + rect.position,
                            new Vector2(x2, yMin) + rect.position,
                            
                            new Vector2(uvMin.x, outer.y),
                            new Vector2(clipped.x, uvMin.y));
                        AddQuad(toFill,
                            new Vector2(x1, yMax) + rect.position,
                            new Vector2(x2, rect.height) + rect.position,
                            
                            new Vector2(uvMin.x, uvMax.y),
                            new Vector2(clipped.x, outer.w));
                    }

                    // Corners
                    AddQuad(toFill,
                        new Vector2(0, 0) + rect.position,
                        new Vector2(xMin, yMin) + rect.position,
                        
                        new Vector2(outer.x, outer.y),
                        new Vector2(uvMin.x, uvMin.y));
                    AddQuad(toFill,
                        new Vector2(xMax, 0) + rect.position,
                        new Vector2(rect.width, yMin) + rect.position,
                        
                        new Vector2(uvMax.x, outer.y),
                        new Vector2(outer.z, uvMin.y));
                    AddQuad(toFill,
                        new Vector2(0, yMax) + rect.position,
                        new Vector2(xMin, rect.height) + rect.position,
                        
                        new Vector2(outer.x, uvMax.y),
                        new Vector2(uvMin.x, outer.w));
                    AddQuad(toFill,
                        new Vector2(xMax, yMax) + rect.position,
                        new Vector2(rect.width, rect.height) + rect.position,
                        
                        new Vector2(uvMax.x, uvMax.y),
                        new Vector2(outer.z, outer.w));
                }
            }
            else
            {
                // Texture has no border, is in repeat mode and not packed. Use texture tiling.
                Vector2 uvScale = new Vector2((xMax - xMin) / tileWidth, (yMax - yMin) / tileHeight);

                if (fillCenter)
                {
                    AddQuad(toFill, new Vector2(xMin, yMin) + rect.position, new Vector2(xMax, yMax) + rect.position,  Vector2.Scale(uvMin, uvScale), Vector2.Scale(uvMax, uvScale));
                }
            }
        }

        private void GenerateFilledSprite(LSVertexHelper toFill) => GenerateFilledSprite(toFill, preserveAspect);
        private void GenerateFilledSprite(LSVertexHelper toFill, bool preserveAspect)
        {
            if (fillAmount < 0.001f)
                return;

            var activeSprite = overrideSprite;
            Vector4 v = GetDrawingDimensions(preserveAspect);
            Vector4 outer = activeSprite != null ? DataUtility.GetOuterUV(activeSprite) : Vector4.zero;
            UIVertex uiv = UIVertex.simpleVert;
            uiv.color = color;

            float tx0 = outer.x;
            float ty0 = outer.y;
            float tx1 = outer.z;
            float ty1 = outer.w;

            // Horizontal and vertical filled sprites are simple -- just end the Image prematurely
            if (fillMethod == FillMethod.Horizontal || fillMethod == FillMethod.Vertical)
            {
                if (fillMethod == FillMethod.Horizontal)
                {
                    float fill = (tx1 - tx0) * fillAmount;

                    if (fillOrigin == 1)
                    {
                        v.x = v.z - (v.z - v.x) * fillAmount;
                        tx0 = tx1 - fill;
                    }
                    else
                    {
                        v.z = v.x + (v.z - v.x) * fillAmount;
                        tx1 = tx0 + fill;
                    }
                }
                else if (fillMethod == FillMethod.Vertical)
                {
                    float fill = (ty1 - ty0) * fillAmount;

                    if (fillOrigin == 1)
                    {
                        v.y = v.w - (v.w - v.y) * fillAmount;
                        ty0 = ty1 - fill;
                    }
                    else
                    {
                        v.w = v.y + (v.w - v.y) * fillAmount;
                        ty1 = ty0 + fill;
                    }
                }
            }

            s_Xy[0] = new Vector3(v.x, v.y);
            s_Xy[1] = new Vector3(v.x, v.w);
            s_Xy[2] = new Vector3(v.z, v.w);
            s_Xy[3] = new Vector3(v.z, v.y);

            s_Uv[0] = new Vector3(tx0, ty0);
            s_Uv[1] = new Vector3(tx0, ty1);
            s_Uv[2] = new Vector3(tx1, ty1);
            s_Uv[3] = new Vector3(tx1, ty0);

            {
                if (fillAmount < 1f && fillMethod != FillMethod.Horizontal && fillMethod != FillMethod.Vertical)
                {
                    if (fillMethod == FillMethod.Radial90)
                    {
                        if (RadialCut(s_Xy, s_Uv, fillAmount, fillClockwise, fillOrigin))
                            AddQuad(toFill, s_Xy,  s_Uv);
                    }
                    else if (fillMethod == FillMethod.Radial180)
                    {
                        for (int side = 0; side < 2; ++side)
                        {
                            float fx0, fx1, fy0, fy1;
                            int even = fillOrigin > 1 ? 1 : 0;

                            if (fillOrigin == 0 || fillOrigin == 2)
                            {
                                fy0 = 0f;
                                fy1 = 1f;
                                if (side == even)
                                {
                                    fx0 = 0f;
                                    fx1 = 0.5f;
                                }
                                else
                                {
                                    fx0 = 0.5f;
                                    fx1 = 1f;
                                }
                            }
                            else
                            {
                                fx0 = 0f;
                                fx1 = 1f;
                                if (side == even)
                                {
                                    fy0 = 0.5f;
                                    fy1 = 1f;
                                }
                                else
                                {
                                    fy0 = 0f;
                                    fy1 = 0.5f;
                                }
                            }

                            s_Xy[0].x = Lerp(v.x, v.z, fx0);
                            s_Xy[1].x = s_Xy[0].x;
                            s_Xy[2].x = Lerp(v.x, v.z, fx1);
                            s_Xy[3].x = s_Xy[2].x;

                            s_Xy[0].y = Lerp(v.y, v.w, fy0);
                            s_Xy[1].y = Lerp(v.y, v.w, fy1);
                            s_Xy[2].y = s_Xy[1].y;
                            s_Xy[3].y = s_Xy[0].y;

                            s_Uv[0].x = Lerp(tx0, tx1, fx0);
                            s_Uv[1].x = s_Uv[0].x;
                            s_Uv[2].x = Lerp(tx0, tx1, fx1);
                            s_Uv[3].x = s_Uv[2].x;

                            s_Uv[0].y = Lerp(ty0, ty1, fy0);
                            s_Uv[1].y = Lerp(ty0, ty1, fy1);
                            s_Uv[2].y = s_Uv[1].y;
                            s_Uv[3].y = s_Uv[0].y;

                            float val = fillClockwise ? fillAmount * 2f - side : fillAmount * 2f - (1 - side);

                            if (RadialCut(s_Xy, s_Uv, Clamp01(val), fillClockwise, (side + fillOrigin + 3) % 4))
                            {
                                AddQuad(toFill, s_Xy,  s_Uv);
                            }
                        }
                    }
                    else if (fillMethod == FillMethod.Radial360)
                    {
                        for (int corner = 0; corner < 4; ++corner)
                        {
                            float fx0, fx1, fy0, fy1;

                            if (corner < 2)
                            {
                                fx0 = 0f;
                                fx1 = 0.5f;
                            }
                            else
                            {
                                fx0 = 0.5f;
                                fx1 = 1f;
                            }

                            if (corner == 0 || corner == 3)
                            {
                                fy0 = 0f;
                                fy1 = 0.5f;
                            }
                            else
                            {
                                fy0 = 0.5f;
                                fy1 = 1f;
                            }

                            //TODO:
                            s_Xy[0].x = Lerp(v.x, v.z, fx0);
                            s_Xy[1].x = s_Xy[0].x;
                            s_Xy[2].x = Lerp(v.x, v.z, fx1);
                            s_Xy[3].x = s_Xy[2].x;

                            s_Xy[0].y = Lerp(v.y, v.w, fy0);
                            s_Xy[1].y = Lerp(v.y, v.w, fy1);
                            s_Xy[2].y = s_Xy[1].y;
                            s_Xy[3].y = s_Xy[0].y;

                            s_Uv[0].x = Lerp(tx0, tx1, fx0);
                            s_Uv[1].x = s_Uv[0].x;
                            s_Uv[2].x = Lerp(tx0, tx1, fx1);
                            s_Uv[3].x = s_Uv[2].x;

                            s_Uv[0].y = Lerp(ty0, ty1, fy0);
                            s_Uv[1].y = Lerp(ty0, ty1, fy1);
                            s_Uv[2].y = s_Uv[1].y;
                            s_Uv[3].y = s_Uv[0].y;

                            float val = fillClockwise ?
                                fillAmount * 4f - (corner + fillOrigin) % 4 :
                                fillAmount * 4f - (3 - (corner + fillOrigin) % 4);

                            if (RadialCut(s_Xy, s_Uv, Clamp01(val), fillClockwise, (corner + 2) % 4))
                                AddQuad(toFill, s_Xy,  s_Uv);
                        }
                    }
                }
                else
                {
                    AddQuad(toFill, s_Xy,  s_Uv);
                }
            }
        }

        private void GenerateFilledSlicedSprite(LSVertexHelper toFill)
        {
            SlicedPrepare(toFill);
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            
            for (int i = 0; i < vertScratch.Length; i++)
            {
                var pos = vertScratch[i];
                
                if (pos.x > maxX) maxX = pos.x;
                if (pos.y > maxY) maxY = pos.y;
            }
            
            var rect = rectTransform.rect;

            for (int x = 0; x < 3; ++x)
            {
                int x2 = x + 1;

                for (int y = 0; y < 3; ++y)
                {
                    if (!fillCenter && x == 1 && y == 1)
                        continue;

                    int y2 = y + 1;
                    
                    var defMin = new Vector2(vertScratch[x].x, vertScratch[y].y);
                    var defMax = new Vector2(vertScratch[x2].x, vertScratch[y2].y);
                    var defUvMin = new Vector2(uVScratch[x].x, uVScratch[y].y);
                    var defUvMax = new Vector2(uVScratch[x2].x, uVScratch[y2].y);
                    
                    var min = defMin;
                    var max = defMax;
                    var uvMin = defUvMin;
                    var uvMax = defUvMax;
                    
                    TryAddQuad();
                    
                    if (mirror.x == 1)
                    {
                        Reset();
                        MirrorX();
                        Reset();
                        if (mirror.y == 1)
                        {
                            MirrorY();
                            Reset();
                            MirrorYWithoutAdd();
                            MirrorX();
                        }
                    }
                    else if (mirror.y == 1)
                    {
                        Reset();
                        MirrorY();
                    }

                    void Reset()
                    {
                        min = defMin;
                        max = defMax;
                        uvMin = defUvMin;
                        uvMax = defUvMax;
                    }
                    
                    void MirrorX()
                    {
                        min.x = maxX + (maxX - min.x);
                        max.x = maxX + (maxX - max.x);
                        (min.x, max.x) = (max.x, min.x);
                        (uvMin.x, uvMax.x) = (uvMax.x, uvMin.x);
                        TryAddQuad();
                    }
                    
                    void MirrorY()
                    {
                        MirrorYWithoutAdd();
                        TryAddQuad();
                    }
                    
                    void MirrorYWithoutAdd()
                    {
                        min.y = maxY + (maxY - min.y);
                        max.y = maxY + (maxY - max.y);
                        (min.y, max.y) = (max.y, min.y);
                        (uvMin.y, uvMax.y) = (uvMax.y, uvMin.y);
                    }
                    
                    void TryAddQuad()
                    {
                        if (CanAddQuad(ref min, ref max, ref uvMin, ref uvMax))
                        {
                            AddQuad(toFill, min, max, uvMin, uvMax);
                        }
                    }
                }
            }

            bool CanAddQuad(ref Vector2 min, ref Vector2 max, ref Vector2 uvMin, ref Vector2 uvMax)
            {
                if (fillMethod == FillMethod.Horizontal)
                {
                    if (fillOrigin == 0) //Left
                    {
                        var maxPoint = rect.xMin + rect.width * fillAmount;
                        var result = min.x < maxPoint;
                        
                        if (result && max.x > maxPoint)
                        {
                            var quadWidth = max.x - min.x;
                            uvMax.x = Lerp(uvMin.x ,uvMax.x, (maxPoint - min.x) / quadWidth);
                            max.x = maxPoint;
                        }
                        
                        return result;
                    }
                    else if(fillOrigin == 1) //Right
                    {
                        var minPoint = rect.xMax - rect.width * fillAmount;
                        var result = max.x > minPoint;
                        
                        if (result && min.x < minPoint)
                        {
                            var quadWidth = max.x - min.x;
                            uvMin.x = Lerp(uvMax.x, uvMin.x, (max.x - minPoint) / quadWidth);
                            min.x = minPoint;
                        }
                        
                        return result;
                    }
                }
                else if(fillMethod == FillMethod.Vertical)
                {
                    if (fillOrigin == 0) //Bottom
                    {
                        var maxPoint = rect.yMin + rect.height * fillAmount;
                        var result = min.y < maxPoint;
                        
                        if (result && max.y > maxPoint)
                        {
                            var quadWidth = max.y - min.y;
                            uvMax.y = Lerp(uvMin.y ,uvMax.y, (maxPoint - min.y) / quadWidth);
                            max.y = maxPoint;
                        }
                        
                        return result;
                    }
                    else if(fillOrigin == 1) //Top
                    {
                        var minPoint = rect.yMax - rect.height * fillAmount;
                        var result = max.y > minPoint;
                        
                        if (result && min.y < minPoint)
                        {
                            var quadWidth = max.y - min.y;
                            uvMin.y = Lerp(uvMax.y, uvMin.y, (max.y - minPoint) / quadWidth);
                            min.y = minPoint;
                        }
                        
                        return result;
                    }
                }

                return true;
            }
        }
        
        //-------------GENERATORS-------------
        
        
        //-------------TOOLS-------------
        
        private Vector4 GetDrawingDimensions(bool shouldPreserveAspect)
        {
            var activeSprite = overrideSprite;
            var padding = activeSprite == null ? Vector4.zero : DataUtility.GetPadding(activeSprite);
            var size = activeSprite == null ? Vector2.zero : new Vector2(activeSprite.rect.width, activeSprite.rect.height);

            Rect rect = GetPixelAdjustedRect();
            TryRotateRect(ref rect);
            currentRect = rect;
            
            int spriteW = RoundToInt(size.x);
            int spriteH = RoundToInt(size.y);

            var v = new Vector4(
                padding.x / spriteW,
                padding.y / spriteH,
                (spriteW - padding.z) / spriteW,
                (spriteH - padding.w) / spriteH);

            if (shouldPreserveAspect && size.sqrMagnitude > 0.0f)
            {
                PreserveSpriteAspectRatio(ref rect, size);
            }
            
            v = new Vector4(
                rect.x + rect.width * v.x,
                rect.y + rect.height * v.y,
                rect.x + rect.width * v.z,
                rect.y + rect.height * v.w
            );

            return v;
        }

        private void PreserveSpriteAspectRatio(ref Rect rect, in Vector2 spriteSize)
        {
            var spriteRatio = spriteSize.x / spriteSize.y;
            var rectRatio = rect.width / rect.height;
            var pivot = rt.pivot;
            
            if(mirror.x == 1) pivot.x *= 2f;
            if(mirror.y == 1) pivot.y *= 2f;

            if (spriteRatio > rectRatio)
            {
                var oldHeight = rect.height;
                rect.height = rect.width * (1.0f / spriteRatio);
                rect.y += (oldHeight - rect.height) * pivot.y;
            }
            else
            {
                var oldWidth = rect.width;
                rect.width = rect.height * spriteRatio;
                rect.x += (oldWidth - rect.width) * pivot.x;
            }
        }
        
        private Vector4 GetAdjustedBorders(Vector4 border, in Rect adjustedRect)
        {
            for (int axis = 0; axis <= 1; axis++)
            {
                float borderScaleRatio;
                
                var size = currentRect.size;
                var size2 = adjustedRect.size;
                
                if (size[axis] != 0)
                {
                    borderScaleRatio = size2[axis] / size[axis];
                    border[axis] *= borderScaleRatio;
                    border[axis + 2] *= borderScaleRatio;
                }
                
                float combinedBorders = border[axis] + border[axis + 2];
                if (size2[axis] < combinedBorders && combinedBorders != 0)
                {
                    borderScaleRatio = size2[axis] / combinedBorders;
                    border[axis] *= borderScaleRatio;
                    border[axis + 2] *= borderScaleRatio;
                }
            }
            return border;
        }
        
        void AddQuad(LSVertexHelper vertexHelper, Vector3[] quadPositions, Vector3[] quadUVs)
        {
            int startIndex = vertexHelper.currentVertCount;

            for (int i = 0; i < 4; ++i)
                AddVert(vertexHelper, quadPositions[i], quadUVs[i]);

            vertexHelper.AddTriangle(startIndex, startIndex + 1, startIndex + 2);
            vertexHelper.AddTriangle(startIndex + 2, startIndex + 3, startIndex);
        }

        void AddQuad(LSVertexHelper vertexHelper, in Vector2 posMin, in Vector2 posMax, in Vector2 uvMin, in Vector2 uvMax)
        {
            int startIndex = vertexHelper.currentVertCount;
            var pos = new Vector3(posMin.x, posMin.y);
            var uv = new Vector4(uvMin.x, uvMin.y);
            AddVert(vertexHelper, pos, uv);
            pos.y = posMax.y;
            uv.y = uvMax.y;
            AddVert(vertexHelper, pos, uv);
            pos.x = posMax.x;
            uv.x = uvMax.x;
            AddVert(vertexHelper, pos, uv);
            pos.y = posMin.y;
            uv.y = uvMin.y;
            AddVert(vertexHelper, pos, uv);

            vertexHelper.AddTriangle(startIndex, startIndex + 1, startIndex + 2);
            vertexHelper.AddTriangle(startIndex + 2, startIndex + 3, startIndex);
        }

        private static Color32 defaultColor;
        
        private void AddVert(in LSVertexHelper vh, in Vector3 position, in Vector4 uv0)
        {
            vh.AddVert(position, defaultColor, uv0);
        }
        
        /// <summary>
        /// Adjust the specified quad, making it be radially filled instead.
        /// </summary>

        static bool RadialCut(Vector3[] xy, Vector3[] uv, float fill, bool invert, int corner)
        {
            // Nothing to fill
            if (fill < 0.001f) return false;

            // Even corners invert the fill direction
            if ((corner & 1) == 1) invert = !invert;

            // Nothing to adjust
            if (!invert && fill > 0.999f) return true;

            // Convert 0-1 value into 0 to 90 degrees angle in radians
            float angle = Clamp01(fill);
            if (invert) angle = 1f - angle;
            angle *= 90f * Deg2Rad;

            // Calculate the effective X and Y factors
            float cos = Cos(angle);
            float sin = Sin(angle);

            RadialCut(xy, cos, sin, invert, corner);
            RadialCut(uv, cos, sin, invert, corner);
            return true;
        }

        /// <summary>
        /// Adjust the specified quad, making it be radially filled instead.
        /// </summary>

        static void RadialCut(Vector3[] xy, float cos, float sin, bool invert, int corner)
        {
            int i0 = corner;
            int i1 = (corner + 1) % 4;
            int i2 = (corner + 2) % 4;
            int i3 = (corner + 3) % 4;

            if ((corner & 1) == 1)
            {
                if (sin > cos)
                {
                    cos /= sin;
                    sin = 1f;

                    if (invert)
                    {
                        xy[i1].x = Lerp(xy[i0].x, xy[i2].x, cos);
                        xy[i2].x = xy[i1].x;
                    }
                }
                else if (cos > sin)
                {
                    sin /= cos;
                    cos = 1f;

                    if (!invert)
                    {
                        xy[i2].y = Lerp(xy[i0].y, xy[i2].y, sin);
                        xy[i3].y = xy[i2].y;
                    }
                }
                else
                {
                    cos = 1f;
                    sin = 1f;
                }

                if (!invert) xy[i3].x = Lerp(xy[i0].x, xy[i2].x, cos);
                else xy[i1].y = Lerp(xy[i0].y, xy[i2].y, sin);
            }
            else
            {
                if (cos > sin)
                {
                    sin /= cos;
                    cos = 1f;

                    if (!invert)
                    {
                        xy[i1].y = Lerp(xy[i0].y, xy[i2].y, sin);
                        xy[i2].y = xy[i1].y;
                    }
                }
                else if (sin > cos)
                {
                    cos /= sin;
                    sin = 1f;

                    if (invert)
                    {
                        xy[i2].x = Lerp(xy[i0].x, xy[i2].x, cos);
                        xy[i3].x = xy[i2].x;
                    }
                }
                else
                {
                    cos = 1f;
                    sin = 1f;
                }

                if (invert) xy[i3].y = Lerp(xy[i0].y, xy[i2].y, sin);
                else xy[i1].x = Lerp(xy[i0].x, xy[i2].x, cos);
            }
        }
    }
}