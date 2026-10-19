namespace Mechanica.Game.Tests;

/// <summary>
/// The Electric Engine's five levels, host plus a client in Survival: the stages of <see cref="GasEngineTest"/> (level 1
/// model, upgrade dialog, level 2 model and name, the <c>electric_engine@1</c> item with its own icon, placing it back).
/// </summary>
public partial class ElectricEngineTest : GasEngineTest
{
    protected override string Tag => "electricengine";

    protected override string PartId => "electric_engine";

    protected override string Title => "Electric Engine";
}
