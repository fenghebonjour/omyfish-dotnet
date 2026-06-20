FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY shared/OMyFish.Shared.BuildingBlocks/OMyFish.Shared.BuildingBlocks.csproj shared/OMyFish.Shared.BuildingBlocks/
COPY shared/OMyFish.Shared.Contracts/OMyFish.Shared.Contracts.csproj shared/OMyFish.Shared.Contracts/
COPY services/ObservationService/OMyFish.ObservationService.Domain/OMyFish.ObservationService.Domain.csproj services/ObservationService/OMyFish.ObservationService.Domain/
COPY services/ObservationService/OMyFish.ObservationService.Application/OMyFish.ObservationService.Application.csproj services/ObservationService/OMyFish.ObservationService.Application/
COPY services/ObservationService/OMyFish.ObservationService.Infrastructure/OMyFish.ObservationService.Infrastructure.csproj services/ObservationService/OMyFish.ObservationService.Infrastructure/
COPY services/ObservationService/OMyFish.ObservationService.Api/OMyFish.ObservationService.Api.csproj services/ObservationService/OMyFish.ObservationService.Api/

RUN dotnet restore services/ObservationService/OMyFish.ObservationService.Api/OMyFish.ObservationService.Api.csproj

COPY . .

RUN dotnet publish services/ObservationService/OMyFish.ObservationService.Api/OMyFish.ObservationService.Api.csproj \
    -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1
COPY --from=build /out .
ENTRYPOINT ["dotnet", "OMyFish.ObservationService.Api.dll"]
