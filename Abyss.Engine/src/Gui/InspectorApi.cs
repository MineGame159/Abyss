namespace Abyss.Engine.Gui;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class Inspectable : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class InspectorFloat : Attribute {
    public readonly float Speed;
    public readonly float Min, Max;

    public InspectorFloat(float speed = 0.1f, float min = float.MinValue, float max = float.MaxValue) {
        Speed = speed;
        Min = min;
        Max = max;
    }
}