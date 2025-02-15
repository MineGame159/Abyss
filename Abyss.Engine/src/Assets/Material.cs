using Abyss.Core;
using Abyss.Engine.Gui;

namespace Abyss.Engine.Assets;

[Inspectable]
public class Material {
    public ITexture? AlbedoMap;
    public Rgba Albedo;
}