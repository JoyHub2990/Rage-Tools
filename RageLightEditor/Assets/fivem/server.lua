RegisterNetEvent('ragetools:restart', function(resource)
    local src = source
    local allowed = GetConvarInt('ragetools_dev', 0) == 1 or IsPlayerAceAllowed(src, 'ragetools')
    if not allowed then
        TriggerClientEvent('ragetools:say', src, 'restart refused - add "set ragetools_dev 1" to server.cfg on a development server')
        return
    end
    if type(resource) ~= 'string' or resource == '' or resource:find('[^%w_%-%.]') then
        TriggerClientEvent('ragetools:say', src, 'bad resource name')
        return
    end
    local state = GetResourceState(resource)
    if state == 'missing' or state == 'unknown' then
        TriggerClientEvent('ragetools:say', src, 'no resource called ' .. resource)
        return
    end
    ExecuteCommand(('restart %s'):format(resource))
    TriggerClientEvent('ragetools:say', src, 'restarted ' .. resource)
end)
