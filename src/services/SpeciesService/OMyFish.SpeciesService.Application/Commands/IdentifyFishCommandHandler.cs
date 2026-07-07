using OMyFish.Shared.BuildingBlocks.CQRS;
using OMyFish.Shared.BuildingBlocks.Messaging;
using OMyFish.SpeciesService.Application.Interfaces;
using OMyFish.SpeciesService.Domain.Entities;
using OMyFish.SpeciesService.Domain.ValueObjects;

namespace OMyFish.SpeciesService.Application.Commands;

internal sealed class IdentifyFishCommandHandler : ICommandHandler<IdentifyFishCommand, IdentifyFishResult>
{
    private readonly IAIServiceClient _aiClient;
    private readonly ISpeciesRepository _speciesRepository;
    private readonly IMessagePublisher _publisher;

    public IdentifyFishCommandHandler(
        IAIServiceClient aiClient,
        ISpeciesRepository speciesRepository,
        IMessagePublisher publisher)
    {
        _aiClient = aiClient;
        _speciesRepository = speciesRepository;
        _publisher = publisher;
    }

    public async Task<IdentifyFishResult> Handle(IdentifyFishCommand command, CancellationToken ct)
    {
        var aiResult = await _aiClient.PredictAsync(command.ImageStorageKey, command.TopK, ct);

        var predictions = new List<PredictionDto>();
        Species? topSpecies = null;

        foreach (var ai in aiResult.Predictions)
        {
            var species = await _speciesRepository.FindByScientificNameAsync(ai.ScientificName, ct)
                ?? Species.Create(ai.ScientificName, ai.CommonName, "Unknown",
                                  "Unknown", "Unknown", "Unknown", "", false);

            var score = ConfidenceScore.Create(ai.Confidence);
            var prediction = species.IdentifyFrom(command.ImageStorageKey, score, ai.Rank);

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
        return new IdentifyFishResult(predictions, uncertain, command.ImageStorageKey, aiResult.IsFish);
    }
}
