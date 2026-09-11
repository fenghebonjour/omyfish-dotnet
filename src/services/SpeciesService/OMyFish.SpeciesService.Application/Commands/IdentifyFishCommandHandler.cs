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
    private readonly IUnitOfWork _unitOfWork;

    public IdentifyFishCommandHandler(
        IAIServiceClient aiClient,
        IStorageService storage,
        ISpeciesRepository speciesRepository,
        IMessagePublisher publisher,
        IUnitOfWork unitOfWork)
    {
        _aiClient = aiClient;
        _storage = storage;
        _speciesRepository = speciesRepository;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
    }

    public async Task<IdentifyFishResult> Handle(IdentifyFishCommand command, CancellationToken ct)
    {
        var storageKey = await _storage.UploadAsync(
            new MemoryStream(command.ImageBytes), command.ImageFileName, command.ImageContentType, ct);

        var imageBase64 = Convert.ToBase64String(command.ImageBytes);
        var aiResult = await _aiClient.PredictAsync(imageBase64, command.TopK, ct);

        var predictions = new List<PredictionDto>();
        Species? topSpecies = null;
        Prediction? topPrediction = null;
        bool topSpeciesIsNew = false;

        // Batched lookup instead of one query per prediction (BACKLOG.md item F, WEAKNESS_AUDIT.md §3.4).
        var scientificNames = aiResult.Predictions.Select(p => p.ScientificName).ToList();
        var knownSpecies = scientificNames.Count == 0
            ? new Dictionary<string, Species>(StringComparer.OrdinalIgnoreCase)
            : (await _speciesRepository.FindByScientificNamesAsync(scientificNames, ct))
                .ToDictionary(s => s.ScientificName, StringComparer.OrdinalIgnoreCase);

        foreach (var ai in aiResult.Predictions)
        {
            var isNew = !knownSpecies.TryGetValue(ai.ScientificName, out var species);
            species ??= Species.Create(ai.ScientificName, ai.CommonName, "Unknown",
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

            if (ai.Rank == 1)
            {
                topSpecies = species;
                topPrediction = prediction;
                topSpeciesIsNew = isNew;
            }
        }

        if (topSpecies is not null)
        {
            // Persists the top prediction (and its species, if the AI service surfaced one
            // outside the catalog) in the same transaction as the event publish below via
            // MassTransit's EF Core outbox, so a crash between "save" and "publish" can no
            // longer drop the event (BACKLOG.md item F §2.3).
            if (topSpeciesIsNew)
                await _speciesRepository.AddAsync(topSpecies, ct);
            await _speciesRepository.AddPredictionAsync(topPrediction!, ct);

            foreach (var evt in topSpecies.PullDomainEvents())
                await _publisher.PublishAsync(evt, ct);

            await _unitOfWork.SaveChangesAsync(ct);
        }

        bool uncertain = predictions.Count == 0 || predictions[0].Confidence < 0.30;
        return new IdentifyFishResult(predictions, uncertain, storageKey, aiResult.IsFish);
    }
}
