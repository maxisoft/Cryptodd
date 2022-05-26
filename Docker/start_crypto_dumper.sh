#!/bin/sh
set -e
PUID=${PUID:-913}
usermod -u "$PUID" "${CRYPTO_DUMPER_USER_NAME:-cryptodumper}" >/dev/null
PGID=${PGID:-913}
groupmod -g "$PGID" "${CRYPTO_DUMPER_GROUP_NAME:-cryptodumper}" >/dev/null
APP_PATH=${APP_PATH:-/app}

CRYPTODUMPER_BASEPATH=${CRYPTODUMPER_BASEPATH:-/cryptodumper}
# create folders
if [ ! -d "${CRYPTODUMPER_BASEPATH}" ]; then \
    mkdir -p "${CRYPTODUMPER_BASEPATH}"
    chown -R "$PUID:$PGID" "${CRYPTODUMPER_BASEPATH}"
fi

# check permissions
if [ ! "$(stat -c %u "${CRYPTODUMPER_BASEPATH}")" = "$PUID" ]; then
	echo "Change in ownership detected, please be patient while we chown existing files ..."
	chown "$PUID:$PGID" -R "${CRYPTODUMPER_BASEPATH}"
fi

renice "+${NICE_ADJUSTEMENT:-1}" $$ >/dev/null 2>&1 || :
exec ionice -c "${IONICE_CLASS:-3}" -n "${IONICE_CLASSDATA:-7}" -t su-exec "$PUID:$PGID" "dotnet" "${APP_PATH}CryptoDumper.Console.dll" $@