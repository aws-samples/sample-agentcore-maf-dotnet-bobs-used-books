FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Packages.props global.json ./
COPY src/BobsBooks.Shared/BobsBooks.Shared.csproj src/BobsBooks.Shared/
COPY src/BobsBooks.Agent/BobsBooks.Agent.csproj src/BobsBooks.Agent/
RUN dotnet restore src/BobsBooks.Agent/BobsBooks.Agent.csproj --runtime linux-arm64
COPY src/BobsBooks.Shared/ src/BobsBooks.Shared/
COPY src/BobsBooks.Agent/ src/BobsBooks.Agent/
RUN dotnet publish src/BobsBooks.Agent/BobsBooks.Agent.csproj \
    --configuration Release \
    --runtime linux-arm64 \
    --self-contained false \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BobsBooks.Agent.dll"]
