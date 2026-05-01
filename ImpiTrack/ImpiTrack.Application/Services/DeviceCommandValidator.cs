using System.Globalization;
using ImpiTrack.Application.Abstractions;
using ImpiTrack.Protocols.Abstractions;

namespace ImpiTrack.Application.Services;

/// <summary>
/// Valida los parametros de comandos de dispositivo aplicando rangos y campos requeridos
/// independientes del protocolo. Stateless, registrar como singleton.
/// </summary>
public sealed class DeviceCommandValidator : IDeviceCommandValidator
{
    private const int MovementRadiusMin = 1;
    private const int MovementRadiusMax = 99999;
    private const int OverspeedMin = 10;
    private const int OverspeedMax = 200;

    /// <inheritdoc />
    public DeviceCommandValidationResult Validate(DeviceCommandType type, IDictionary<string, object>? parameters)
    {
        return type switch
        {
            DeviceCommandType.SetMovementAlarm => ValidateMovementAlarm(parameters),
            DeviceCommandType.SetOverspeedAlarm => ValidateOverspeedAlarm(parameters),
            DeviceCommandType.SetGeofence => ValidateGeofence(parameters),
            // Comandos sin parametros requeridos: aceptamos parametros vacios o null.
            _ => DeviceCommandValidationResult.Valid
        };
    }

    private static DeviceCommandValidationResult ValidateMovementAlarm(IDictionary<string, object>? parameters)
    {
        if (!TryReadInt(parameters, "radiusMeters", out int radius))
        {
            return DeviceCommandValidationResult.Invalid("parameter_missing field=radiusMeters");
        }

        if (radius < MovementRadiusMin || radius > MovementRadiusMax)
        {
            return DeviceCommandValidationResult.Invalid(
                $"parameter_out_of_range field=radiusMeters min={MovementRadiusMin} max={MovementRadiusMax}");
        }

        return DeviceCommandValidationResult.Valid;
    }

    private static DeviceCommandValidationResult ValidateOverspeedAlarm(IDictionary<string, object>? parameters)
    {
        if (!TryReadInt(parameters, "speedKmh", out int speed))
        {
            return DeviceCommandValidationResult.Invalid("parameter_missing field=speedKmh");
        }

        if (speed < OverspeedMin || speed > OverspeedMax)
        {
            return DeviceCommandValidationResult.Invalid(
                $"parameter_out_of_range field=speedKmh min={OverspeedMin} max={OverspeedMax}");
        }

        return DeviceCommandValidationResult.Valid;
    }

    private static DeviceCommandValidationResult ValidateGeofence(IDictionary<string, object>? parameters)
    {
        if (!TryReadDouble(parameters, "latTL", out double latTL)) return DeviceCommandValidationResult.Invalid("parameter_missing field=latTL");
        if (!TryReadDouble(parameters, "lonTL", out double lonTL)) return DeviceCommandValidationResult.Invalid("parameter_missing field=lonTL");
        if (!TryReadDouble(parameters, "latBR", out double latBR)) return DeviceCommandValidationResult.Invalid("parameter_missing field=latBR");
        if (!TryReadDouble(parameters, "lonBR", out double lonBR)) return DeviceCommandValidationResult.Invalid("parameter_missing field=lonBR");

        if (!IsValidLatitude(latTL)) return DeviceCommandValidationResult.Invalid("parameter_out_of_range field=latTL min=-90 max=90");
        if (!IsValidLatitude(latBR)) return DeviceCommandValidationResult.Invalid("parameter_out_of_range field=latBR min=-90 max=90");
        if (!IsValidLongitude(lonTL)) return DeviceCommandValidationResult.Invalid("parameter_out_of_range field=lonTL min=-180 max=180");
        if (!IsValidLongitude(lonBR)) return DeviceCommandValidationResult.Invalid("parameter_out_of_range field=lonBR min=-180 max=180");

        return DeviceCommandValidationResult.Valid;
    }

    private static bool IsValidLatitude(double value) => value >= -90d && value <= 90d;

    private static bool IsValidLongitude(double value) => value >= -180d && value <= 180d;

    private static bool TryReadInt(IDictionary<string, object>? parameters, string key, out int value)
    {
        value = 0;
        if (parameters is null) return false;
        if (!parameters.TryGetValue(key, out object? raw) || raw is null) return false;

        switch (raw)
        {
            case int direct:
                value = direct;
                return true;
            case long longVal when longVal >= int.MinValue && longVal <= int.MaxValue:
                value = (int)longVal;
                return true;
            case double doubleVal when doubleVal >= int.MinValue && doubleVal <= int.MaxValue:
                value = (int)doubleVal;
                return true;
            case decimal decimalVal when decimalVal >= int.MinValue && decimalVal <= int.MaxValue:
                value = (int)decimalVal;
                return true;
            case string str when int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
    }

    private static bool TryReadDouble(IDictionary<string, object>? parameters, string key, out double value)
    {
        value = 0d;
        if (parameters is null) return false;
        if (!parameters.TryGetValue(key, out object? raw) || raw is null) return false;

        switch (raw)
        {
            case double direct:
                value = direct;
                return true;
            case float floatVal:
                value = floatVal;
                return true;
            case int intVal:
                value = intVal;
                return true;
            case long longVal:
                value = longVal;
                return true;
            case decimal decimalVal:
                value = (double)decimalVal;
                return true;
            case string str when double.TryParse(str, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed):
                value = parsed;
                return true;
            default:
                return false;
        }
    }
}
