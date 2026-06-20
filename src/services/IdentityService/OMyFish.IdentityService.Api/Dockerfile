FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY shared/OMyFish.Shared.BuildingBlocks/OMyFish.Shared.BuildingBlocks.csproj shared/OMyFish.Shared.BuildingBlocks/
COPY shared/OMyFish.Shared.Contracts/OMyFish.Shared.Contracts.csproj shared/OMyFish.Shared.Contracts/
COPY services/IdentityService/OMyFish.IdentityService.Domain/OMyFish.IdentityService.Domain.csproj services/IdentityService/OMyFish.IdentityService.Domain/
COPY services/IdentityService/OMyFish.IdentityService.Infrastructure/OMyFish.IdentityService.Infrastructure.csproj services/IdentityService/OMyFish.IdentityService.Infrastructure/
COPY services/IdentityService/OMyFish.IdentityService.Api/OMyFish.IdentityService.Api.csproj services/IdentityService/OMyFish.IdentityService.Api/

RUN dotnet restore services/IdentityService/OMyFish.IdentityService.Api/OMyFish.IdentityService.Api.csproj

COPY . .

RUN dotnet publish services/IdentityService/OMyFish.IdentityService.Api/OMyFish.IdentityService.Api.csproj \
    -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1
COPY --from=build /out .
ENTRYPOINT ["dotnet", "OMyFish.IdentityService.Api.dll"]
