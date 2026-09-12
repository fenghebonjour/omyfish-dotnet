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
    private readonly IPredictionRepository _predictionRepository;
    private readonly IMessagePublisher _publisher;
    private readonly IUnitOfWork _unitOfWork;

    public IdentifyFishCommandHandler(
        IAIServiceClient aiClient,
        IStorageService storage,
        ISpeciesRepository speciesRepository,
        IPredictionRepository predictionRepository,
        IMessagePublisher publisher,
        IUnitOfWork unitOfWork)
    {
        _aiClient = aiClient;
        _storage = storage;
        _speciesRepository = speciesRepository;
        _predictionRepository = predictionRepository;
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
            // Species now lives in MongoDB (BACKLOG.md item E) — a species the AI service
            // surfaced outside the catalog is written there immediately, independent of the
            // transaction below. If this succeeds but the prediction/publish below then fails,
            // the result is an orphaned catalog entry with no prediction — an acceptable
            // inconsistency for read-mostly reference data with no relational integrity needs
            // (the same reasoning item E itself is built on), unlike dropping the event itself.
            if (topSpeciesIsNew)
                await _speciesRepository.AddAsync(topSpecies, ct);

            // The prediction and the event publish still commit together in one Postgres
            // transaction via MassTransit's EF Core outbox, so a crash between "save" and
            // "publish" can't drop the event (BACKLOG.md item F §2.3).
            await _predictionRepository.AddPredictionAsync(topPrediction!, ct);

            foreach (var evt in topSpecies.PullDomainEvents())
                await _publisher.PublishAsync(evt, ct);

            await _unitOfWork.SaveChangesAsync(ct);
        }

        bool uncertain = predictions.Count == 0 || predictions[0].Confidence < 0.30;
        return new IdentifyFishResult(predictions, uncertain, storageKey, aiResult.IsFish);
    }
}
