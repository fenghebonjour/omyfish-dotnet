using NSubstitute;
using OMyFish.Shared.BuildingBlocks.Domain;
using OMyFish.Shared.BuildingBlocks.Messaging;
using OMyFish.SpeciesService.Application.Commands;
using OMyFish.SpeciesService.Application.Interfaces;
using OMyFish.SpeciesService.Domain.Entities;
using Xunit;

namespace OMyFish.SpeciesService.Tests;

public class IdentifyFishCommandHandlerTests
{
    private readonly IAIServiceClient _ai = Substitute.For<IAIServiceClient>();
    private readonly ISpeciesRepository _repo = Substitute.For<ISpeciesRepository>();
    private readonly IMessagePublisher _publisher = Substitute.For<IMessagePublisher>();
    private readonly IdentifyFishCommandHandler _handler;

    private static readonly IdentifyFishCommand Command = new("img/key.jpg", 3, Guid.NewGuid());

    public IdentifyFishCommandHandlerTests()
    {
        _handler = new IdentifyFishCommandHandler(_ai, _repo, _publisher);
    }

    private void AiReturns(params AIPrediction[] predictions) =>
        _ai.PredictAsync("img/key.jpg", 3, Arg.Any<CancellationToken>())
            .Returns(new AIServiceResult(predictions, IsFish: predictions.Length > 0));

    [Fact]
    public async Task Handle_UsesCatalogSpeciesWhenKnown()
    {
        var walleye = Species.Create("Sander vitreus", "Walleye", "Percidae",
            "LC", "Lake", "NA", "Desc", true);
        _repo.FindByScientificNameAsync("Sander vitreus", Arg.Any<CancellationToken>())
            .Returns(walleye);
        AiReturns(new AIPrediction("Sander vitreus", "Walleye", 0.91, 1));

        var result = await _handler.Handle(Command, CancellationToken.None);

        var top = Assert.Single(result.Predictions);
        Assert.Equal("Walleye", top.SpeciesName);
        Assert.Equal("Sander vitreus", top.ScientificName);
        Assert.Equal(0.91, top.Confidence);
        Assert.Equal(1, top.Rank);
        Assert.False(result.Uncertain);
        Assert.True(result.IsFish);
    }

    [Fact]
    public async Task Handle_CreatesFallbackSpeciesWhenUnknownToCatalog()
    {
        AiReturns(new AIPrediction("Esox masquinongy", "Muskellunge", 0.55, 1));

        var result = await _handler.Handle(Command, CancellationToken.None);

        var top = Assert.Single(result.Predictions);
        Assert.Equal("Muskellunge", top.SpeciesName);
        Assert.Equal("Esox masquinongy", top.ScientificName);
    }

    [Fact]
    public async Task Handle_PublishesEventForTopPrediction()
    {
        AiReturns(
            new AIPrediction("Sander vitreus", "Walleye", 0.91, 1),
            new AIPrediction("Perca flavescens", "Yellow Perch", 0.05, 2));

        await _handler.Handle(Command, CancellationToken.None);

        await _publisher.Received(1)
            .PublishAsync(Arg.Any<DomainEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoPredictions_UncertainAndNoEvent()
    {
        AiReturns();

        var result = await _handler.Handle(Command, CancellationToken.None);

        Assert.Empty(result.Predictions);
        Assert.True(result.Uncertain);
        Assert.False(result.IsFish);
        await _publisher.DidNotReceive()
            .PublishAsync(Arg.Any<DomainEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LowTopConfidence_FlagsUncertain()
    {
        AiReturns(new AIPrediction("Sander vitreus", "Walleye", 0.12, 1));

        var result = await _handler.Handle(Command, CancellationToken.None);

        Assert.True(result.Uncertain);
    }

    [Fact]
    public async Task Handle_NotAFish_RejectsWithoutTouchingCatalogOrPublishing()
    {
        // Edge case: a cat photo — the AI service's CLIP gate rejects it upstream.
        _ai.PredictAsync("img/key.jpg", 3, Arg.Any<CancellationToken>())
            .Returns(new AIServiceResult(Array.Empty<AIPrediction>(), IsFish: false));

        var result = await _handler.Handle(Command, CancellationToken.None);

        Assert.False(result.IsFish);
        Assert.Empty(result.Predictions);
        Assert.True(result.Uncertain);
        await _repo.DidNotReceive()
            .FindByScientificNameAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _publisher.DidNotReceive()
            .PublishAsync(Arg.Any<DomainEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TopConfidenceExactlyAtThreshold_NotUncertain()
    {
        AiReturns(new AIPrediction("Sander vitreus", "Walleye", 0.30, 1));

        var result = await _handler.Handle(Command, CancellationToken.None);

        Assert.False(result.Uncertain);
    }

    [Fact]
    public async Task Handle_AIMetadataOverridesCatalogFields()
    {
        var walleye = Species.Create("Sander vitreus", "Walleye", "Percidae",
            "LC", "Lake", "NA", "Catalog description", true);
        _repo.FindByScientificNameAsync("Sander vitreus", Arg.Any<CancellationToken>())
            .Returns(walleye);
        AiReturns(new AIPrediction("Sander vitreus", "Walleye", 0.91, 1,
            ConservationStatus: "Near Threatened", Habitat: "River",
            Diet: "Minnows", MaxSizeCm: 107, Description: "AI description", FunFact: "Glows"));

        var result = await _handler.Handle(Command, CancellationToken.None);

        var top = Assert.Single(result.Predictions);
        Assert.Equal("Near Threatened", top.ConservationStatus);
        Assert.Equal("River", top.Habitat);
        Assert.Equal("Minnows", top.Diet);
        Assert.Equal(107, top.MaxSizeCm);
        Assert.Equal("AI description", top.Description);
        Assert.Equal("Glows", top.FunFact);
    }

    [Fact]
    public async Task Handle_NullAIMetadataFallsBackToCatalog()
    {
        var walleye = Species.Create("Sander vitreus", "Walleye", "Percidae",
            "LC", "Lake", "NA", "Catalog description", true);
        _repo.FindByScientificNameAsync("Sander vitreus", Arg.Any<CancellationToken>())
            .Returns(walleye);
        AiReturns(new AIPrediction("Sander vitreus", "Walleye", 0.91, 1));

        var result = await _handler.Handle(Command, CancellationToken.None);

        var top = Assert.Single(result.Predictions);
        Assert.Equal("LC", top.ConservationStatus);
        Assert.Equal("Lake", top.Habitat);
        Assert.Equal("Catalog description", top.Description);
    }
}
