FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY shared/OMyFish.Shared.Contracts/OMyFish.Shared.Contracts.csproj shared/OMyFish.Shared.Contracts/
COPY services/NotificationService/OMyFish.NotificationService/OMyFish.NotificationService.csproj services/NotificationService/OMyFish.NotificationService/

RUN dotnet restore services/NotificationService/OMyFish.NotificationService/OMyFish.NotificationService.csproj

COPY . .

RUN dotnet publish services/NotificationService/OMyFish.NotificationService/OMyFish.NotificationService.csproj \
    -c Release -o /out

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /out .
ENTRYPOINT ["dotnet", "OMyFish.NotificationService.dll"]
