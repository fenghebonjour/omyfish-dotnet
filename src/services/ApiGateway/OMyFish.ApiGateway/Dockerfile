FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY services/ApiGateway/OMyFish.ApiGateway/OMyFish.ApiGateway.csproj services/ApiGateway/OMyFish.ApiGateway/

RUN dotnet restore services/ApiGateway/OMyFish.ApiGateway/OMyFish.ApiGateway.csproj

COPY . .

RUN dotnet publish services/ApiGateway/OMyFish.ApiGateway/OMyFish.ApiGateway.csproj \
    -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
HEALTHCHECK --interval=30s --timeout=10s --retries=3 \
  CMD wget -qO- http://localhost:8080/health || exit 1
COPY --from=build /out .
ENTRYPOINT ["dotnet", "OMyFish.ApiGateway.dll"]
