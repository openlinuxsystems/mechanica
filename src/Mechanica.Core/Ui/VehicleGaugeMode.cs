namespace Mechanica.Core.Ui;

/// <summary>How the HUD shows a seated player's vehicle gauges (Options, Interface; <see cref="Items.VehicleGauges"/>).</summary>
public enum VehicleGaugeMode
{
    /// <summary>No gauges.</summary>
    Off,

    /// <summary>One row per container, with its name.</summary>
    PerContainer,

    /// <summary>One row per kind, its containers summed, with no name.</summary>
    PerType,
}
