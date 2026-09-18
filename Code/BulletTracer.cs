using Sandbox;

public sealed class BulletTracer : Component
{
    public Vector3 StartPoint { get; set; }
    public Vector3 EndPoint { get; set; }

    private float _lifetime = 0.12f;
    private float _elapsed = 0f;

    protected override void OnUpdate()
    {
        _elapsed += Time.Delta;

        // Считаем прозрачность — трейсер быстро исчезает
        float alpha = 1f - (_elapsed / _lifetime);

        // Рисуем линию выстрела через Gizmo
        using ( Gizmo.Scope( "tracer", global::Transform.Zero ) )
        {
            Gizmo.Draw.IgnoreDepth = true;
            Gizmo.Draw.Color = Color.Yellow.WithAlpha( alpha );
            Gizmo.Draw.Line( StartPoint, EndPoint );

            // Небольшая вспышка в точке попадания
            Gizmo.Draw.Color = Color.Orange.WithAlpha( alpha * 0.8f );
            Gizmo.Draw.LineSphere( EndPoint, 3f );
        }

        if ( _elapsed >= _lifetime )
            GameObject.Destroy();
    }
}