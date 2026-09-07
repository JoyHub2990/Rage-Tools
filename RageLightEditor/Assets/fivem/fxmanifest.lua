fx_version 'cerulean'
game 'gta5'
lua54 'yes'

author 'RAGE Tools'
description 'RAGE Tools live link - mirrors the editor in the game'
version '1.0.0'

ui_page 'html/index.html'

files {
    'html/index.html',
    'html/bridge.js',
}

shared_script 'config.lua'
client_script 'client.lua'
server_script 'server.lua'
