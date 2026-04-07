FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

COPY ["OctoSync.csproj", "./"]
RUN dotnet restore "OctoSync.csproj"

COPY . .
RUN dotnet publish "OctoSync.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Install Playwright browsers and capture dependencies
RUN dotnet tool install --global Microsoft.Playwright.CLI && \
	/root/.dotnet/tools/playwright install --with-deps chromium 2>&1 | tee /deps.log

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

COPY --from=build /app/publish .
COPY --from=build /ms-playwright /ms-playwright

# Install Chromium runtime dependencies
RUN apt-get update && \
	apt-get install -y --no-install-recommends \
		ca-certificates \
		ffmpeg \
		fonts-liberation \
		libappindicator3-1 \
		libasound2t64 \
		libatk-bridge2.0-0 \
		libatk1.0-0 \
		libcups2 \
		libgbm1 \
		libglib2.0-0 \
		libgtk-3-0 \
		libpango-1.0-0 \
		libpangocairo-1.0-0 \
		libx11-6 \
		libx11-xcb1 \
		libxcb1 \
		libxcomposite1 \
		libxcursor1 \
		libxdamage1 \
		libxext6 \
		libxfixes3 \
		libxi6 \
		libxinerama1 \
		libxrandr2 \
		libxrender1 \
		libxss1 \
		libxtst6 \
		libnss3 \
		libnspr4 && \
	chmod -R a+rX /ms-playwright && \
	apt-get clean && \
	rm -rf /var/lib/apt/lists/*

# Set folder permissions recursively
RUN chmod 755 -R /app

ENV DOTNET_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "OctoSync.dll"]
