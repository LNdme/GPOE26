FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY GpoeMcpTestServer.csproj .
RUN dotnet restore GpoeMcpTestServer.csproj
COPY . .
RUN dotnet publish GpoeMcpTestServer.csproj -c Release -o /out --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:10.0
USER 65532:65532
WORKDIR /app
COPY --from=build --chown=65532:65532 /out .
ENTRYPOINT ["dotnet", "GpoeMcpTestServer.dll"]
