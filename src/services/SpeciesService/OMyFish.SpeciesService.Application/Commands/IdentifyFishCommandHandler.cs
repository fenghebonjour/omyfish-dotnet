using OMyFish.Shared.BuildingBlocks.CQRS;
using OMyFish.Shared.BuildingBlocks.Messaging;
using OMyFish.SpeciesService.Application.Interfaces;
using OMyFish.SpeciesService.Domain.Entities;
using OMyFish.SpeciesService.Domain.ValueObjects;

namespace OMyFish.SpeciesService.Application.Commands;

internal sealed class IdentifyFishCommandHandler : ICommandHandler<IdentifyFishCommand, IdentifyFishResult>
{
    private readonly IAIServiceClient _aiClient;
    private readonly IStorageService _storage;
    private readonly ISpeciesRepository _speciesRepository;
    private readonly IMessagePublisher _publisher;

    public IdentifyFishCommandHandler(
        IAIServiceClient aiClient,
        IStorageService storage,
        ISpeciesRepository speciesRepository,
        IMessagePublisher publisher)
    {
        _aiClient = aiClient;
        _storage = storage;
        _speciesRepository = speciesRepository;
        _publisher = publisher;
    }

    public async Task<IdentifyFishResult> Handle(IdentifyFishCommand command, CancellationToken ct)
    {
        var storageKey = await _storage.UploadAsync(
            new MemoryStream(command.ImageBytes), command.ImageFileName, command.ImageContentType, ct);

        var imageBase64 = Convert.ToBase64String(command.ImageBytes);
        var aiResult = await _aiClient.PredictAsync(imageBase64, command.TopK, ct);

        var predictions = new List<PredictionDto>();
        Species? topSpecies = null;

        // Batched lookup instead of one query per prediction (BACKLOG.md item F, WEAKNESS_AUDIT.md §3.4).
        var scientificNames = aiResult.Predictions.Select(p => p.ScientificName).ToList();
        var knownSpecies = scientificNames.Count == 0
            ? new Dictionary<string, Species>(StringComparer.OrdinalIgnoreCase)
            : (await _speciesRepository.FindByScientificNamesAsync(scientificNames, ct))
                .ToDictionary(s => s.ScientificName, StringComparer.OrdinalIgnoreCase);

        foreach (var ai in aiResult.Predictions)
        {
            var species = knownSpecies.GetValueOrDefault(ai.ScientificName)
                ?? Species.Create(ai.ScientificName, ai.CommonName, "Unknown",
                                  "Unknown", "Unknown", "Unknown", "", false);

            var score = ConfidenceScore.Create(ai.Confidence);
            var prediction = species.IdentifyFrom(storageKey, score, ai.Rank);

            predictions.Add(new PredictionDto(
                species.CommonName, species.ScientificName,
                score.Value, score.AsPercent(), ai.Rank,
                ai.ConservationStatus ?? species.ConservationStatus,
                ai.Habitat ?? species.Habitat,
                ai.Diet,
                ai.MaxSizeCm,
                ai.Description ?? species.Description,
                ai.FunFact));

            if (ai.Rank == 1) topSpecies = species;
        }

        if (topSpecies is not null)
        {
            var domainEvents = topSpecies.PullDomainEvents();
            foreach (var evt in domainEvents)
                await _publisher.PublishAsync(evt, ct);
        }

        bool uncertain = predictions.Count == 0 || predictions[0].Confidence < 0.30;
        return new IdentifyFishResult(predictions, uncertain, storageKey, aiResult.IsFish);
    }
}
