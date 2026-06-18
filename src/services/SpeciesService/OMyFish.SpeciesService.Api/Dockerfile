FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY shared/OMyFish.Shared.BuildingBlocks/OMyFish.Shared.BuildingBlocks.csproj shared/OMyFish.Shared.BuildingBlocks/
COPY shared/OMyFish.Shared.Contracts/OMyFish.Shared.Contracts.csproj shared/OMyFish.Shared.Contracts/
COPY services/SpeciesService/OMyFish.SpeciesService.Domain/OMyFish.SpeciesService.Domain.csproj services/SpeciesService/OMyFish.SpeciesService.Domain/
COPY services/SpeciesService/OMyFish.SpeciesService.Application/OMyFish.SpeciesService.Application.csproj services/SpeciesService/OMyFish.SpeciesService.Application/
COPY services/SpeciesService/OMyFish.SpeciesService.Infrastructure/OMyFish.SpeciesService.Infrastructure.csproj services/SpeciesService/OMyFish.SpeciesService.Infrastructure/
COPY services/SpeciesService/OMyFish.SpeciesService.Api/OMyFish.SpeciesService.Api.csproj services/SpeciesService/OMyFish.SpeciesService.Api/

RUN dotnet restore services/SpeciesService/OMyFish.SpeciesService.Api/OMyFish.SpeciesService.Api.csproj

COPY . .

RUN dotnet publish services/SpeciesService/OMyFish.SpeciesService.Api/OMyFish.SpeciesService.Api.csproj \
    -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1
COPY --from=build /out .
ENTRYPOINT ["dotnet", "OMyFish.SpeciesService.Api.dll"]
