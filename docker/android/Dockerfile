FROM mcr.microsoft.com/dotnet/sdk:10.0

ARG GODOT_VERSION=4.7.2
ARG ANDROID_CMDLINE_TOOLS=13114758
ARG ANDROID_BUILD_TOOLS=36.1.0
ARG ANDROID_PLATFORM=android-36

RUN apt-get update \
 && apt-get install -y --no-install-recommends openjdk-17-jdk-headless unzip wget ca-certificates fontconfig \
 && rm -rf /var/lib/apt/lists/*

ENV JAVA_HOME=/usr/lib/jvm/java-17-openjdk-amd64 \
    ANDROID_HOME=/opt/android-sdk \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1

RUN mkdir -p $ANDROID_HOME/cmdline-tools \
 && wget -q "https://dl.google.com/android/repository/commandlinetools-linux-${ANDROID_CMDLINE_TOOLS}_latest.zip" -O /tmp/cmdline.zip \
 && unzip -q /tmp/cmdline.zip -d $ANDROID_HOME/cmdline-tools \
 && mv $ANDROID_HOME/cmdline-tools/cmdline-tools $ANDROID_HOME/cmdline-tools/latest \
 && rm /tmp/cmdline.zip \
 && yes | $ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager --licenses > /dev/null \
 && $ANDROID_HOME/cmdline-tools/latest/bin/sdkmanager "platform-tools" "build-tools;${ANDROID_BUILD_TOOLS}" "platforms;${ANDROID_PLATFORM}" > /dev/null

RUN wget -q "https://github.com/godotengine/godot/releases/download/${GODOT_VERSION}-stable/Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64.zip" -O /tmp/godot.zip \
 && unzip -q /tmp/godot.zip -d /opt \
 && mv /opt/Godot_v${GODOT_VERSION}-stable_mono_linux_x86_64 /opt/godot \
 && ln -s /opt/godot/Godot_v${GODOT_VERSION}-stable_mono_linux.x86_64 /usr/local/bin/godot \
 && rm /tmp/godot.zip

COPY entrypoint.sh /usr/local/bin/entrypoint.sh
RUN chmod +x /usr/local/bin/entrypoint.sh

WORKDIR /project
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
