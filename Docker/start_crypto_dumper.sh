#!/bin/sh
set -e
PUID=${PUID:-913}
usermod -u "$PUID" "${CRYPTO_DUMPER_USER_NAME:-cryptodd}" >/dev/null
PGID=${PGID:-913}
groupmod -g "$PGID" "${CRYPTO_DUMPER_GROUP_NAME:-cryptodd}" >/dev/null
APP_PATH=${APP_PATH:-/app}

CRYPTOdd_BASEPATH=${CRYPTOdd_BASEPATH:-/cryptodd}
# create folders
if [ ! -d "${CRYPTOdd_BASEPATH}" ]; then \
    mkdir -p "${CRYPTOdd_BASEPATH}"
    chown -R "$PUID:$PGID" "${CRYPTOdd_BASEPATH}"
fi

# check permissions
if [ ! "$(stat -c %u "${CRYPTOdd_BASEPATH}")" = "$PUID" ]; then
	echo "Change in ownership detected, please be patient while we chown existing files ..."
	chown "$PUID:$PGID" -R "${CRYPTOdd_BASEPATH}"
fi

renice "+${NICE_ADJUSTEMENT:-1}" $$ >/dev/null 2>&1 || :
exec ionice -c "${IONICE_CLASS:-3}" -n "${IONICE_CLASSDATA:-7}" -t su-exec "$PUID:$PGID" "dotnet" "${APP_PATH}/Cryptodd.Console.dll" $@