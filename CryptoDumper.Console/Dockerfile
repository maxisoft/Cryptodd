FROM mcr.microsoft.com/dotnet/runtime:6.0-alpine AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine AS build
WORKDIR /src
COPY ["CryptoDumper.Console/CryptoDumper.Console.csproj", "CryptoDumper.Console/", "CryptoDumper/", "Maxisoft.Plugins/", "Maxisoft.Utils/"]
RUN dotnet restore "CryptoDumper.Console/CryptoDumper.Console.csproj"
COPY . .
WORKDIR "/src/CryptoDumper.Console"
RUN dotnet build "CryptoDumper.Console.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CryptoDumper.Console.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CryptoDumper.Console.dll"]
