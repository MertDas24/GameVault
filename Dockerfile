# syntax=docker/dockerfile:1
# Çok aşamalı build. Sırlar imaja gömülmez; tüm yapılandırma çalışma zamanında
# ortam değişkenleriyle verilir (bkz. docs/OPERATIONS.md).

# --- Derleme ---
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src

# Önce yalnızca proje ve kilit dosyaları: restore katmanı önbelleğe alınır.
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY .config/dotnet-tools.json .config/
COPY GameVault.Entities/GameVault.Entities.csproj GameVault.Entities/packages.lock.json GameVault.Entities/
COPY GameVault.DataAccess/GameVault.DataAccess.csproj GameVault.DataAccess/packages.lock.json GameVault.DataAccess/
COPY GameVault.Models/GameVault.Models.csproj GameVault.Models/packages.lock.json GameVault.Models/
COPY GameVault.Business/GameVault.Business.csproj GameVault.Business/packages.lock.json GameVault.Business/
COPY GameVault.Web/GameVault.Web.csproj GameVault.Web/packages.lock.json GameVault.Web/
RUN dotnet restore GameVault.Web/GameVault.Web.csproj --locked-mode

COPY GameVault.Entities/ GameVault.Entities/
COPY GameVault.DataAccess/ GameVault.DataAccess/
COPY GameVault.Models/ GameVault.Models/
COPY GameVault.Business/ GameVault.Business/
COPY GameVault.Web/ GameVault.Web/
RUN dotnet publish GameVault.Web/GameVault.Web.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# --- Migration paketi (ayrı imaj; uygulamadan önce bir kez çalıştırılır) ---
FROM build AS migrations-build
RUN dotnet tool restore \
 && dotnet ef migrations bundle --project GameVault.DataAccess --startup-project GameVault.DataAccess \
      --configuration Release -o /out/efbundle

FROM mcr.microsoft.com/dotnet/runtime:10.0.12 AS migrator
COPY --from=migrations-build /out/efbundle /efbundle
USER $APP_UID
# Kullanım: docker run --rm <imaj> --connection "<bağlantı dizesi>"
ENTRYPOINT ["/efbundle"]

# --- Çalışma zamanı (varsayılan hedef) ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0.12 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_NOLOGO=1
COPY --from=build /app .
# Resmi .NET imajlarındaki root olmayan kullanıcı (UID 1654).
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "GameVault.Web.dll"]
