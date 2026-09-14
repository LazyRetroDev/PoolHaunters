using UnityEngine;
using UnityEngine.UI;
using TMPro;

public enum ControllerIconStyle { Xbox, PlayStation, Nintendo }

// UI geometry stays sharp at different menu scales; no texture atlas required.
[RequireComponent(typeof(CanvasRenderer))]
public class ControllerButtonIcon : MaskableGraphic
{
    private string control = "buttonSouth";
    private TMP_Text caption;
    private static readonly Color Ink = new Color(0.93f, 0.95f, 0.98f);
    private static readonly Color Back = new Color(0.055f, 0.065f, 0.085f);

    public static ControllerButtonIcon Create(Transform parent)
    {
        var go = new GameObject("Controller Button Icon", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var icon = go.AddComponent<ControllerButtonIcon>();
        icon.raycastTarget = false;
        icon.rectTransform.sizeDelta = new Vector2(44f, 28f);
        return icon;
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        GameSettingsManager.ControllerIconsChanged += RefreshIcon;
        if (caption == null)
        {
            var go = new GameObject("Button Letter", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(transform, false);
            caption = go.GetComponent<TMP_Text>();
            caption.rectTransform.anchorMin = Vector2.zero;
            caption.rectTransform.anchorMax = Vector2.one;
            caption.rectTransform.offsetMin = caption.rectTransform.offsetMax = Vector2.zero;
            caption.alignment = TextAlignmentOptions.Center;
            caption.fontStyle = FontStyles.Bold;
            caption.raycastTarget = false;
            caption.enableWordWrapping = false;
        }
        RefreshIcon();
    }

    protected override void OnDisable()
    {
        GameSettingsManager.ControllerIconsChanged -= RefreshIcon;
        base.OnDisable();
    }

    public void SetControl(string path)
    {
        if (control == path) return;
        control = path;
        RefreshIcon();
    }

    public static string FaceLetter(string path, ControllerIconStyle style)
    {
        bool nintendo = style == ControllerIconStyle.Nintendo;
        switch (path)
        {
            case "buttonSouth": return nintendo ? "B" : "A";
            case "buttonEast": return nintendo ? "A" : "B";
            case "buttonWest": return nintendo ? "Y" : "X";
            case "buttonNorth": return nintendo ? "X" : "Y";
            default: return "";
        }
    }

    void RefreshIcon()
    {
        if (caption == null) return;
        var style = GameSettingsManager.ControllerIcons;
        string text = "";
        Color tint = Ink;
        bool face = control.StartsWith("button");
        if (face && style != ControllerIconStyle.PlayStation)
        {
            text = FaceLetter(control, style);
            if (style == ControllerIconStyle.Xbox)
            {
                switch (control)
                {
                    case "buttonSouth": tint = new Color(0.45f, 0.9f, 0.3f); break;
                    case "buttonEast": tint = new Color(1f, 0.38f, 0.35f); break;
                    case "buttonWest": tint = new Color(0.35f, 0.7f, 1f); break;
                    case "buttonNorth": tint = new Color(1f, 0.86f, 0.25f); break;
                }
            }
        }
        bool ps = style == ControllerIconStyle.PlayStation;
        bool ns = style == ControllerIconStyle.Nintendo;
        switch (control)
        {
            case "leftShoulder": text = ps ? "L1" : ns ? "L" : "LB"; break;
            case "rightShoulder": text = ps ? "R1" : ns ? "R" : "RB"; break;
            case "leftTrigger": text = ps ? "L2" : ns ? "ZL" : "LT"; break;
            case "rightTrigger": text = ps ? "R2" : ns ? "ZR" : "RT"; break;
            case "leftStickPress": text = ps ? "L3" : ns ? "L" : "LS"; break;
            case "rightStickPress": text = ps ? "R3" : ns ? "R" : "RS"; break;
        }
        caption.text = text;
        caption.color = tint;
        caption.fontSize = text.Length > 1 ? 13f : 17f;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        Rect rect = GetPixelAdjustedRect();
        float scale = Mathf.Min(rect.width / 44f, rect.height / 28f);
        Vector2 center = rect.center;
        bool round = control.StartsWith("button") || control.EndsWith("StickPress");
        if (round)
        {
            Disk(vh, center, 13f * scale, Ink);
            Disk(vh, center, 11.5f * scale, Back);
        }
        else
        {
            RoundedBox(vh, center, new Vector2(40, 25) * scale, 5f * scale, Ink);
            RoundedBox(vh, center, new Vector2(37, 22) * scale, 4f * scale, Back);
        }

        if (GameSettingsManager.ControllerIcons == ControllerIconStyle.PlayStation && control.StartsWith("button"))
        {
            float h = 6.5f * scale, width = 1.8f * scale;
            Color symbol = control == "buttonSouth" ? new Color(0.5f,0.75f,1f) :
                control == "buttonEast" ? new Color(1f,0.45f,0.45f) :
                control == "buttonWest" ? new Color(1f,0.6f,0.85f) : new Color(0.45f,1f,0.8f);
            if (control == "buttonEast")
            {
                Disk(vh, center, h, symbol); Disk(vh, center, h-width, Back);
            }
            else if (control == "buttonSouth")
            {
                Line(vh,center+new Vector2(-h,-h),center+new Vector2(h,h),width,symbol);
                Line(vh,center+new Vector2(-h,h),center+new Vector2(h,-h),width,symbol);
            }
            else if (control == "buttonWest")
            {
                Line(vh,center+new Vector2(-h,-h),center+new Vector2(h,-h),width,symbol);
                Line(vh,center+new Vector2(h,-h),center+new Vector2(h,h),width,symbol);
                Line(vh,center+new Vector2(h,h),center+new Vector2(-h,h),width,symbol);
                Line(vh,center+new Vector2(-h,h),center+new Vector2(-h,-h),width,symbol);
            }
            else
            {
                Vector2 a=center+new Vector2(0,h+1*scale), b=center+new Vector2(-h,-h), c=center+new Vector2(h,-h);
                Line(vh,a,b,width,symbol); Line(vh,b,c,width,symbol); Line(vh,c,a,width,symbol);
            }
        }
        if (control.StartsWith("dpad/"))
        {
            Color dim = new Color(0.3f,0.34f,0.4f);
            Line(vh,center+Vector2.left*8*scale,center+Vector2.right*8*scale,6*scale,dim);
            Line(vh,center+Vector2.down*8*scale,center+Vector2.up*8*scale,6*scale,dim);
            Vector2 direction=control.EndsWith("left")?Vector2.left:control.EndsWith("right")?Vector2.right:control.EndsWith("up")?Vector2.up:Vector2.down;
            Vector2 tip=center+direction*9*scale, side=new Vector2(-direction.y,direction.x)*4*scale;
            Triangle(vh,tip,center+direction*3*scale+side,center+direction*3*scale-side,Ink);
        }
        if (control == "start")
        {
            if (GameSettingsManager.ControllerIcons == ControllerIconStyle.Nintendo)
            { Line(vh,center+Vector2.left*7*scale,center+Vector2.right*7*scale,2*scale,Ink); Line(vh,center+Vector2.down*7*scale,center+Vector2.up*7*scale,2*scale,Ink); }
            else for(int i=-1;i<=1;i++) Line(vh,center+new Vector2(-7,i*4)*scale,center+new Vector2(7,i*4)*scale,1.6f*scale,Ink);
        }
    }

    static void Triangle(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Color tint)
    {
        int n=vh.currentVertCount;
        vh.AddVert(a,tint,Vector2.zero); vh.AddVert(b,tint,Vector2.zero); vh.AddVert(c,tint,Vector2.zero);
        vh.AddTriangle(n,n+1,n+2);
    }
    static void Line(VertexHelper vh, Vector2 a, Vector2 b, float width, Color tint)
    {
        Vector2 direction=(b-a).normalized, normal=new Vector2(-direction.y,direction.x)*width*0.5f;
        Triangle(vh,a-normal,a+normal,b+normal,tint); Triangle(vh,a-normal,b+normal,b-normal,tint);
    }
    static void Disk(VertexHelper vh, Vector2 center, float radius, Color tint)
    {
        for(int i=0;i<48;i++)
        {
            float a=i*Mathf.PI*2/48,b=(i+1)*Mathf.PI*2/48;
            Triangle(vh,center,center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,tint);
        }
    }
    static void RoundedBox(VertexHelper vh, Vector2 center, Vector2 size, float radius, Color tint)
    {
        Vector2 previous=Vector2.zero,first=Vector2.zero;
        for(int i=0;i<36;i++)
        {
            int corner=i/9;float angle=(corner*90+(i%9)*90f/8)*Mathf.Deg2Rad;
            Vector2 offset=new Vector2(corner==0||corner==3?1:-1,corner<2?1:-1);
            Vector2 point=center+Vector2.Scale(offset,size*0.5f-Vector2.one*radius)+new Vector2(Mathf.Cos(angle),Mathf.Sin(angle))*radius;
            if(i==0)first=point;else Triangle(vh,center,previous,point,tint);
            previous=point;
        }
        Triangle(vh,center,previous,first,tint);
    }
}
