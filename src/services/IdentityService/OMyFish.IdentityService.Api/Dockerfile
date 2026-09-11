FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/shared/OMyFish.Shared.BuildingBlocks/OMyFish.Shared.BuildingBlocks.csproj src/shared/OMyFish.Shared.BuildingBlocks/
COPY src/shared/OMyFish.Shared.Contracts/OMyFish.Shared.Contracts.csproj src/shared/OMyFish.Shared.Contracts/
COPY src/services/IdentityService/OMyFish.IdentityService.Domain/OMyFish.IdentityService.Domain.csproj src/services/IdentityService/OMyFish.IdentityService.Domain/
COPY src/services/IdentityService/OMyFish.IdentityService.Infrastructure/OMyFish.IdentityService.Infrastructure.csproj src/services/IdentityService/OMyFish.IdentityService.Infrastructure/
COPY src/services/IdentityService/OMyFish.IdentityService.Api/OMyFish.IdentityService.Api.csproj src/services/IdentityService/OMyFish.IdentityService.Api/

RUN dotnet restore src/services/IdentityService/OMyFish.IdentityService.Api/OMyFish.IdentityService.Api.csproj

COPY . .

RUN dotnet publish src/services/IdentityService/OMyFish.IdentityService.Api/OMyFish.IdentityService.Api.csproj \
    -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
RUN apt-get update && apt-get install -y --no-install-recommends wget \
    && rm -rf /var/lib/apt/lists/*
HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1
COPY --from=build /out .
# Run as the non-root user baked into the .NET runtime image instead of root
# (BACKLOG.md item F, WEAKNESS_AUDIT.md §1.4).
USER $APP_UID
ENTRYPOINT ["dotnet", "OMyFish.IdentityService.Api.dll"]
