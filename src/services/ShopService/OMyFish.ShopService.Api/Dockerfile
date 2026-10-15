FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/shared/OMyFish.Shared.BuildingBlocks/OMyFish.Shared.BuildingBlocks.csproj src/shared/OMyFish.Shared.BuildingBlocks/
COPY src/services/ShopService/OMyFish.ShopService.Domain/OMyFish.ShopService.Domain.csproj src/services/ShopService/OMyFish.ShopService.Domain/
COPY src/services/ShopService/OMyFish.ShopService.Application/OMyFish.ShopService.Application.csproj src/services/ShopService/OMyFish.ShopService.Application/
COPY src/services/ShopService/OMyFish.ShopService.Infrastructure/OMyFish.ShopService.Infrastructure.csproj src/services/ShopService/OMyFish.ShopService.Infrastructure/
COPY src/services/ShopService/OMyFish.ShopService.Api/OMyFish.ShopService.Api.csproj src/services/ShopService/OMyFish.ShopService.Api/

RUN dotnet restore src/services/ShopService/OMyFish.ShopService.Api/OMyFish.ShopService.Api.csproj

COPY . .

RUN dotnet publish src/services/ShopService/OMyFish.ShopService.Api/OMyFish.ShopService.Api.csproj \
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
ENTRYPOINT ["dotnet", "OMyFish.ShopService.Api.dll"]
