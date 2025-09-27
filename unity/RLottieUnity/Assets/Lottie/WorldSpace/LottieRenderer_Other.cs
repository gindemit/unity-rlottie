using LSCore;
using LSCore.Extensions;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class LottieRenderer
{
    static LottieRenderer()
    {
        vertexHelper.Init();
    }
    
    private Mesh quad;
    private static Material unlitMat;
    private static readonly int mainTexId = Shader.PropertyToID("_MainTex");
    private static readonly LSVertexHelper vertexHelper = new();
    [SerializeField] [HideInInspector] private int rotateId = 0;
    [SerializeField] [HideInInspector] private int pixelsPerUnit = 128;
    [SerializeField] [HideInInspector] private Vector2Int flip;
    private MeshRenderer mr;
    private MeshFilter mf;
    private MaterialPropertyBlock mpb;
    
    [SerializeField] [HideInInspector] private Color color = Color.white;
    public Color Color
    {
        get => color;
        set
        {
            if(color == value) return;
            color = value;
            MarkColorAsDirty();
        }
    }
    
    [SerializeField] [HideInInspector] private Material material;
    public Material Material
    {
        get => material;
        set
        {
            if (material == value) return;
            material = value;
            mr.sharedMaterial = value;
        }
    }
    
    public int PixelsPerUnit
    {
        get => pixelsPerUnit;
        set
        {
            var val = Mathf.Clamp(value, MinSize, MaxSize);
            if (val == pixelsPerUnit) return;
            pixelsPerUnit = val;
            manager.ResizeIfNeeded();
        }
    }
    
    public (bool x, bool y) Flip
    {
        get => (flip.x.ToBool(), flip.y.ToBool());
        set
        {
            var newValue = new Vector2Int(value.x.ToInt(), value.y.ToInt());
            if(flip == newValue) return;
            flip = newValue; 
            MarkMeshAsDirty();
        }
    }
    
    public LSImage.RotationMode Rotation
    {
        get => (LSImage.RotationMode)rotateId;
        set
        {
            var newValue = (int)value;
            if(rotateId == newValue) return;
            rotateId = newValue;
            MarkMeshAsDirty();
        }
    }

    #region MESH_BUILDING

    private void UpdateColor()
    {
        var v = quad.colors;
        for (int i = 0; i < v.Length; i++)
        {
            v[i] = color;
        }
        quad.colors = v;
    }
    
    private void BuildUnitQuad()
    {
        if (quad == null) quad = new Mesh { name = "LottieWorld_UnitQuad" };

        var vh = vertexHelper;
        var v = UIVertex.simpleVert;
        v.color = color;
        Vector2 uvMin, uvMax;
        float x = 1; float y = 1;
        
        if (sprite == null)
        {
            uvMin = Vector2.zero;
            uvMax = Vector2.one;
        }
        else
        {
            uvMin = Sprite.UvMin;
            uvMax = Sprite.UvMax;
            if (sprite.Aspect > 1)
            {
                x = 1;
                y = 1 / sprite.Aspect;
            }
            else
            {
                y = 1;
                x = 1 / sprite.Aspect;
            }
        }

        x /= 2;
        y /= 2;
        
        v.position = new Vector3(-x, -y, 0f);
        v.uv0 = uvMin;
        vh.AddVert(v);

        v.position = new Vector3(-x, y, 0f);
        v.uv0 = new Vector2(uvMin.x, uvMax.y);
        vh.AddVert(v);

        v.position = new Vector3(x, y, 0f);
        v.uv0 = uvMax;
        vh.AddVert(v);

        v.position = new Vector3(x, -y, 0f);
        v.uv0 = new Vector2(uvMax.x, uvMin.y);
        vh.AddVert(v);

        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(0, 2, 3);

        RotateMesh(vh);
        vh.FillMesh(quad);
        vh.Clear();
        quad.RecalculateBounds();
        mf.sharedMesh = quad;
    }

    public delegate void RotateAction(ref Vector3 value, in Vector2 center);

    private RotateAction rotateAction;

    private void RotateMesh(LSVertexHelper vh)
    {
        if (rotateId == 0 && flip is { x: 0, y: 0 }) return;

        UIVertex vert = new UIVertex();
        var count = vh.currentVertCount;
        var center = new Vector2(0, 0);

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
                vh.AddTriangle(i + 1, i + 2, i + 3);
                vh.AddTriangle(i + 3, i, i + 1);
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

    #endregion
}