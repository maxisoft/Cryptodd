FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY ["Cryptodd.Console/Cryptodd.Console.csproj", "Cryptodd.Console/"]
RUN dotnet restore "Cryptodd.Console/Cryptodd.Console.csproj"
COPY . .
WORKDIR "/src/Cryptodd.Console"
RUN dotnet build "Cryptodd.Console.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Cryptodd.Console.csproj" -c Release -o /app/publish --no-self-contained --os linux-musl

FROM base AS final
ARG maintainer="maxisoft"
LABEL maintainer="${maintainer}" name="cryptodd" description="Save usefull crypto data into databases" url="https://github.com/${maintainer}/Cryptodd" vcs-url="https://github.com/${maintainer}/Cryptodd" org.opencontainers.image.source="https://github.com/${maintainer}/Cryptodd"
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_RUNNING_IN_CONTAINER=true DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false CRYPTODD_ScanForAssemblyConfig=false 
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Cryptodd.Console.dll"]
