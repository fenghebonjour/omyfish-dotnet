namespace OMyFish.SpeciesService.Application.DTOs;

// Mirrors omyfish-ai's /regs/* responses (Quebec fishing regs/consumption
// advisory feature — chatbot/retrieval logic stays in omyfish-ai, this is
// just a proxy).

public sealed record RegsSpeciesLimitDto(
    string Species,
    string Period,
    string CatchLimit,
    string? LengthLimit,
    string? FishingDevice,
    string? Note);

public sealed record RegsLimitsDto(
    double Lat,
    double Lon,
    string ZoneName,
    string? ZoneInfoUrl,
    IReadOnlyList<RegsSpeciesLimitDto> Rules,
    string Disclaimer);

public sealed record RegsStationDto(
    string NoBqma,
    string Hydronyme,
    double Latitude,
    double Longitude,
    double DistanceKm);

public sealed record RegsConsumptionDto(
    double Lat,
    double Lon,
    string Species,
    string StationName,
    double DistanceKm,
    string? SizeClass,
    int? MealsPerMonth,
    string? FishingStatus,
    string? Note,
    string Disclaimer);

public sealed record RegsAnswerDto(
    string Question,
    string Answer,
    IReadOnlyList<string> Sources,
    string Disclaimer);
