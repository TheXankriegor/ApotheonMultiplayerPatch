FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY ["ApotheonMasterServer.csproj", "./"]
RUN dotnet restore "ApotheonMasterServer.csproj"

# Copy everything else and build
COPY . .
RUN dotnet publish "ApotheonMasterServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Build runtime image
FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app/publish .

# Expose the default master server UDP port
EXPOSE 14343/udp

ENTRYPOINT ["dotnet", "ApotheonMasterServer.dll"]
