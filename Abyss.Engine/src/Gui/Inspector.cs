using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Abyss.Core;
using Abyss.Engine.Assets;
using Arch.Core;
using Arch.Core.Extensions;
using Hexa.NET.ImGui;
using Silk.NET.Maths;

namespace Abyss.Engine.Gui;

internal static class Inspector {
    public static void Render(EntityReference entity) {
        if (!ImGui.Begin("Inspector")) {
            ImGui.End();
            return;
        }

        if (entity.IsAlive()) {
            var i = 0;

            foreach (var component in entity.Entity.GetAllComponents()) {
                if (component == null)
                    continue;

                if (i++ > 0)
                    ImGui.Separator();

                if (ImGui.TreeNodeEx(component.GetType().Name, ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanFullWidth)) {
                    if (RenderFields(component))
                        entity.Entity.Set(component);

                    ImGui.TreePop();
                }
            }
        }

        ImGui.End();
    }

    private static readonly Dictionary<Type, Func<IInspectorField, bool>> fieldRenderers = new() {
        { typeof(bool), RenderBoolField },

        { typeof(byte), RenderIntNField<byte, byte> },
        { typeof(Vector2D<byte>), RenderIntNField<Vector2D<byte>, byte> },
        { typeof(Vector3D<byte>), RenderIntNField<Vector3D<byte>, byte> },
        { typeof(Vector4D<byte>), RenderIntNField<Vector4D<byte>, byte> },
        { typeof(Rgba), RenderIntNField<Rgba, byte> },

        { typeof(short), RenderIntNField<short, short> },
        { typeof(Vector2D<short>), RenderIntNField<Vector2D<short>, short> },
        { typeof(Vector3D<short>), RenderIntNField<Vector3D<short>, short> },
        { typeof(Vector4D<short>), RenderIntNField<Vector4D<short>, short> },

        { typeof(int), RenderIntNField<int, int> },
        { typeof(Vector2D<int>), RenderIntNField<Vector2D<int>, int> },
        { typeof(Vector3D<int>), RenderIntNField<Vector3D<int>, int> },
        { typeof(Vector4D<int>), RenderIntNField<Vector4D<int>, int> },

        { typeof(float), RenderFloatNField<float> },
        { typeof(Vector2), RenderFloatNField<Vector2> },
        { typeof(Vector3), RenderFloatNField<Vector3> },
        { typeof(Vector4), RenderFloatNField<Vector4> },
        { typeof(Quaternion), RenderFloatNField<Quaternion> },

        { typeof(string), RenderStringField }
    };

    private static Func<IInspectorField, bool>? GetRenderer(Type type) {
        if (fieldRenderers.TryGetValue(type, out var renderer))
            return renderer;

        if (type.IsAssignableTo(typeof(IMesh)) || type.IsAssignableTo(typeof(ITexture))) {
            return field => {
                var obj = field.Get<object?>();
                ImGui.Text(obj?.GetHashCode().ToString("X") ?? "null");
                return false;
            };
        }

        if (type.GetCustomAttribute<Inspectable>() != null) {
            return field => {
                var changed = false;

                if (ImGui.TreeNodeEx("", ImGuiTreeNodeFlags.SpanFullWidth)) {
                    var obj = field.Get<object>();

                    if (RenderFields(obj)) {
                        field.Set(obj);
                        changed = true;
                    }

                    ImGui.TreePop();
                }

                return changed;
            };
        }

        return null;
    }

    private static bool RenderFields(object obj) {
        var nameWidth = 0f;

        foreach (var field in Fields(obj)) {
            nameWidth = Math.Max(nameWidth, ImGui.CalcTextSize(field.Name).X);
        }

        var style = ImGui.GetStyle();
        var changed = false;

        foreach (var field in Fields(obj)) {
            ImGui.BeginGroup();

            ImGui.AlignTextToFramePadding();
            ImGui.Text(field.Name);
            ImGui.SameLine(nameWidth, style.ItemInnerSpacing.X);

            ImGui.PushID(field.Name);
            changed |= GetRenderer(field.Type)!(field);
            ImGui.PopID();

            ImGui.EndGroup();
        }

        return changed;
    }

    private static bool RenderBoolField(IInspectorField field) {
        return RenderFieldWrapper(field, (ref bool v) => ImGui.Checkbox("", ref v));
    }

    private static bool RenderIntNField<T, T2>(IInspectorField field)
        where T : unmanaged where T2 : unmanaged, IBinaryInteger<T2>, IMinMaxValue<T2> {
        return RenderFieldWrapper(field, (ref T v) => RenderIntN(MemoryMarshal.Cast<T, T2>(new Span<T>(ref v))));
    }

    private static bool RenderFloatNField<T>(IInspectorField field) where T : unmanaged {
        return RenderFieldWrapper(field, (ref T v) => {
            var speed = 0.1f;
            var min = float.MinValue;
            var max = float.MaxValue;

            if (field.GetAttribute(out InspectorFloat flt)) {
                speed = flt.Speed;
                min = flt.Min;
                max = flt.Max;
            }

            return RenderFloatN(MemoryMarshal.Cast<T, float>(new Span<T>(ref v)), speed, min, max);
        });
    }

    private static bool RenderStringField(IInspectorField field) {
        return RenderFieldWrapper(field, (ref string str) => {
            var strLen = Encoding.UTF8.GetByteCount(str);
            Span<byte> bytes = stackalloc byte[Math.Max(512, strLen + 128)];

            Encoding.UTF8.TryGetBytes(str, bytes, out _);
            bytes[strLen] = 0;

            var changed = ImGui.InputText("", ref bytes.GetPinnableReference(), (ulong) bytes.Length);

            if (changed)
                str = Encoding.UTF8.GetString(bytes[..bytes.IndexOf((byte) 0)]);

            return changed;
        });
    }

    private delegate bool FieldWrapper<T>(ref T value);

    private static bool RenderFieldWrapper<T>(IInspectorField field, FieldWrapper<T> wrapper) {
        var value = field.Get<T>();
        var changed = wrapper(ref value);

        if (changed)
            field.Set(value);

        return changed;
    }

    private static bool RenderIntN<T>(Span<T> values) where T : IBinaryInteger<T>, IMinMaxValue<T> {
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var changed = false;

        ImGuiP.PushMultiItemsWidths(values.Length, ImGui.GetContentRegionAvail().X);

        for (var i = 0; i < values.Length; i++) {
            ImGui.PushID(i);

            if (i > 0)
                ImGui.SameLine(0, spacing);

            var v = int.CreateChecked(values[i]);

            if (ImGui.DragInt("", ref v, 0.25f, int.CreateTruncating(T.MinValue), int.CreateTruncating(T.MaxValue))) {
                values[i] = T.CreateTruncating(v);
                changed = true;
            }

            ImGui.PopID();
            ImGui.PopItemWidth();
        }

        return changed;
    }

    private static bool RenderFloatN(Span<float> values, float speed, float min, float max) {
        var spacing = ImGui.GetStyle().ItemInnerSpacing.X;
        var changed = false;

        ImGuiP.PushMultiItemsWidths(values.Length, ImGui.GetContentRegionAvail().X);

        for (var i = 0; i < values.Length; i++) {
            ImGui.PushID(i);

            if (i > 0)
                ImGui.SameLine(0, spacing);

            if (ImGui.DragFloat("", ref values[i], speed, min, max)) {
                changed = true;
            }

            ImGui.PopID();
            ImGui.PopItemWidth();
        }

        return changed;
    }

    private static IEnumerable<IInspectorField> Fields(object obj) {
        foreach (var field in obj.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public)) {
            if (GetRenderer(field.FieldType) != null) {
                yield return new FieldInspectorField(obj, field);
            }
        }

        foreach (var property in obj.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
            if (GetRenderer(property.PropertyType) != null && property.GetMethod != null && property.SetMethod != null) {
                yield return new PropertyInspectorField(obj, property);
            }
        }
    }
}

internal interface IInspectorField {
    string Name { get; }

    Type Type { get; }

    T Get<T>();

    void Set<T>(T value);

    bool GetAttribute<T>(out T attribute) where T : Attribute;
}

internal class FieldInspectorField : IInspectorField {
    private readonly object obj;
    private readonly FieldInfo field;

    public FieldInspectorField(object obj, FieldInfo field) {
        this.obj = obj;
        this.field = field;
    }

    public string Name => field.Name;

    public Type Type => field.FieldType;

    public T Get<T>() {
        return (T) field.GetValue(obj)!;
    }

    public void Set<T>(T value) {
        field.SetValue(obj, value);
    }

    public bool GetAttribute<T>(out T attribute) where T : Attribute {
        var a = field.GetCustomAttribute<T>();

        if (a != null) {
            attribute = a;
            return true;
        }

        attribute = null!;
        return false;
    }
}

internal class PropertyInspectorField : IInspectorField {
    private readonly object obj;
    private readonly PropertyInfo property;

    public PropertyInspectorField(object obj, PropertyInfo property) {
        this.obj = obj;
        this.property = property;
    }

    public string Name => property.Name;

    public Type Type => property.PropertyType;

    public T Get<T>() {
        return (T) property.GetValue(obj)!;
    }

    public void Set<T>(T value) {
        property.SetValue(obj, value);
    }

    public bool GetAttribute<T>(out T attribute) where T : Attribute {
        var a = property.GetCustomAttribute<T>();

        if (a != null) {
            attribute = a;
            return true;
        }

        attribute = null!;
        return false;
    }
}