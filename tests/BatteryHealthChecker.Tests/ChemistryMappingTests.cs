using BatteryHealthChecker.Collectors;
using BatteryHealthChecker.Models;
using Xunit;

namespace BatteryHealthChecker.Tests;

/// <summary>Chemistry and status code mapping from the two WMI/ACPI vocabularies.</summary>
public class ChemistryMappingTests
{
    [Theory]
    [InlineData("LION", BatteryChemistry.LithiumIon)]
    [InlineData("LiON", BatteryChemistry.LithiumIon)]
    [InlineData("NiMH", BatteryChemistry.NickelMetalHydride)]
    [InlineData("NiCd", BatteryChemistry.NickelCadmium)]
    [InlineData("NiZn", BatteryChemistry.NickelZinc)]
    [InlineData("PbAc", BatteryChemistry.LeadAcid)]
    [InlineData("RAM", BatteryChemistry.RechargeableAlkalineManganese)]
    public void Maps_the_four_character_ACPI_chemistry_codes(string code, BatteryChemistry expected) =>
        Assert.Equal(expected, IoctlBatteryCollector.MapChemistry(code));

    [Fact]
    public void An_unknown_code_maps_to_Other_so_the_raw_value_can_still_be_shown()
    {
        Assert.Equal(BatteryChemistry.Other, IoctlBatteryCollector.MapChemistry("ZZZZ"));
    }

    [Theory]
    [InlineData(6, BatteryChemistry.LithiumIon)]
    [InlineData(8, BatteryChemistry.LithiumPolymer)]
    [InlineData(5, BatteryChemistry.NickelMetalHydride)]
    [InlineData(3, BatteryChemistry.LeadAcid)]
    [InlineData(2, BatteryChemistry.Unknown)]
    [InlineData(99, BatteryChemistry.Unknown)]
    public void Maps_the_CIM_chemistry_enumeration(int value, BatteryChemistry expected) =>
        Assert.Equal(expected, Win32BatteryCollector.MapCimChemistry(value));

    [Theory]
    [InlineData(1, ChargeState.Discharging, AcPowerState.Disconnected)]
    [InlineData(2, ChargeState.NotCharging, AcPowerState.Connected)]
    [InlineData(3, ChargeState.FullyCharged, AcPowerState.Unknown)]
    [InlineData(6, ChargeState.Charging, AcPowerState.Connected)]
    [InlineData(10, ChargeState.Unknown, AcPowerState.Unknown)]
    public void Maps_Win32_Battery_status_codes(int code, ChargeState state, AcPowerState ac)
    {
        (ChargeState actualState, AcPowerState actualAc) = Win32BatteryCollector.MapBatteryStatus(code);

        Assert.Equal(state, actualState);
        Assert.Equal(ac, actualAc);
    }
}
