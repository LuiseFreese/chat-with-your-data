-- Maps user principal names to the regions they may see (consumed by the 'Regional Reader' RLS role).
-- Populated by 'Northstar.Fabric.Tool seed' from the NORTHSTAR_RLS_USERS environment variable,
-- e.g. NORTHSTAR_RLS_USERS="viewer.user@yourtenant.example=West;manager.user@yourtenant.example=North|South"
DELETE FROM cwd.RegionAccess;
-- INSERT INTO cwd.RegionAccess ([User Principal Name],[Region]) VALUES
-- ('viewer.user@yourtenant.example','West'),
-- ('manager.user@yourtenant.example','North'),
-- ('manager.user@yourtenant.example','South');