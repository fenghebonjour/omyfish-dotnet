namespace OMyFish.SpeciesService.Application.DTOs;

public sealed record SpeciesDto(
    Guid Id,
    string ScientificName,
    string CommonName,
    string Family,
    string ConservationStatus,
    string Habitat,
    string GeographicRange,
    string Description,
    bool IsNorthAmericanFreshwater,
    string? ImageUrl);
