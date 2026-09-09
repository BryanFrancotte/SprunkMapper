using SharpDX;

namespace CodeWalker
{
    /// <summary>
    /// Implemented by a tool panel that wants to drive the world view's transform widget as a
    /// group gizmo - moving a whole set of files at once - instead of the widget acting on the
    /// current map selection. While a target is registered with WorldForm.ShowGroupWidget(),
    /// widget drags are routed here and the normal selection-editing path is bypassed entirely.
    /// </summary>
    public interface IGroupWidgetTarget
    {
        void OnGroupWidgetPositionChange(Vector3 newpos, Vector3 oldpos);
        void OnGroupWidgetRotationChange(Quaternion newrot, Quaternion oldrot);
    }
}
