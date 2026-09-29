using UnityEngine;
using UnityEngine.EventSystems;

/// Joystick virtual analógico. Estructura UI: "Background" (Image, con este script) -> hijo "Handle" (Image).
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public static VirtualJoystick Instance { get; private set; }
    public RectTransform handle;
    public float radius = 80f;
    public Vector2 Value { get; private set; }

    RectTransform bg;

    void Awake() { Instance = this; bg = (RectTransform)transform; }

    public void OnPointerDown(PointerEventData e) { OnDrag(e); }

    public void OnDrag(PointerEventData e)
    {
        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(bg, e.position, e.pressEventCamera, out local);
        Vector2 clamped = Vector2.ClampMagnitude(local, radius);
        Value = clamped / radius;
        if (handle != null) handle.anchoredPosition = clamped;
    }

    public void OnPointerUp(PointerEventData e)
    {
        Value = Vector2.zero;
        if (handle != null) handle.anchoredPosition = Vector2.zero;
    }
}
