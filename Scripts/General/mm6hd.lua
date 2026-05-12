
-- sprites scale
if path.FindFirst(AppPath.."data/hd.sprites.lod") then
	function events.GameInitialized2()
		for _, a in Game.SFTBin do
			local name = a.SpriteName:sub(1, 7):lower()
			if name ~= "newhand" and name ~= "newglas" then
				a.Scale = a.Scale/2
			end
		end
	end
	-- monster info scale
	mem.prot(true)
	mem.u4[0x41CFE6] = 0x08000
	mem.u4[0x41D052] = 0x4B9358
	mem.u4[0x41D05A] = 0x4B93D0
	mem.prot(false)
end

if not path.FindFirst(AppPath.."data/hd.bitmaps.lod") then
	return
end

BitmapsHDScale = 2
local buf = mem.StaticAlloc(8)
local hooks = HookManager{U = buf, V = buf + 4}

-- mul U, V vectors
hooks.asmhook(0x478D40, [[
	off equ esp + 4
	macro Param
	{
		mov eax, [off]
		add [off], eax
		off equ off+4
	}
	Param
	Param
	Param
	Param
	Param
	Param
]])

-- mul BitmapU, BitmapV of facet
hooks.asmhook(0x469BE7, [[
	mov eax, [ebx+0x1C]
	add [ebx+0x1C], eax
	mov eax, [ebx+0x20]
	add [ebx+0x20], eax
]])

-- for fun
-- hooks.asmhook(0x471EA1, [[
-- 	add ebx, ebx
-- ]])

-- mul U, V limits (tiles)
local code = [[
	off equ 0x72F8E8
	macro Param
	{
		mov eax, [off]
		add [off], eax
		off equ off+4
	}
	add dword [off], 0x8000
	Param
	add dword [off], 0x8000
	Param
	Param
	Param
	
	; fix overflows
	macro Over
	{
		local @ok
		local @bad
		mov dword [buf], 0
		mov eax, [off]
		add eax, 0x100000
		jo @bad
		cmp dword [off + 8], 0x80000000
		jnz @ok
	@bad:
		mov eax, 0x80000000
		mov [buf], eax
		add [off], eax
		add [off + 8], eax
	@ok:
		off equ off+4
		buf equ buf+4
	}
	off equ off-16
	buf equ %U%
	Over
	Over
]]
hooks.asmhook(0x479AAA, code)
hooks.asmhook(0x479BBD, code)
hooks.asmhook(0x479EEE, code)

-- mul U, V limits (facets)
local code = [[
	add edx, edx
	add ecx, ecx
	add ebp, ebp
	add eax, eax
]]
hooks.asmhook(0x479DB8, code)

-- fix flat tiles
local code = [[
	add eax, eax
]]
hooks.asmhook2(0x477652, code)  -- Flat
hooks.asmhook2(0x477D6D, code)  -- Transparent
hooks.asmhook2(0x47347C, code)  -- Water
hooks.asmhook2(0x472F8A, code)  -- Horizontal facet
hooks.asmhook2(0x478354, code)  -- sky
hooks.asmhook2(0x472646, code)  -- long range ground

-- fix int overflow for tiles
local code1 = [[
	add eax, [%U%]
]]
local code2 = [[
	add eax, [%V%]
]]
hooks.asmhook2(0x4775D3, code1)  -- Flat
hooks.asmhook2(0x4775E7, code2)  -- Flat
hooks.asmhook2(0x477CEE, code1)  -- Transparent
hooks.asmhook2(0x477D02, code2)  -- Transparent
hooks.asmhook2(0x4764D4, code1)  -- Non-Flat
hooks.asmhook2(0x4764E9, code2)  -- Non-Flat
hooks.asmhook2(0x476607, code1)  -- Non-Flat
hooks.asmhook2(0x476629, code2)  -- Non-Flat
hooks.asmhook2(0x476ADC, code1)  -- Non-Flat
hooks.asmhook2(0x476B0C, code2)  -- Non-Flat
hooks.asmhook2(0x476FB2, code1)  -- Non-Flat
hooks.asmhook2(0x476FE2, code2)  -- Non-Flat
hooks.asmhook2(0x473BDC, code1)  -- Non-Flat Water (not used)
hooks.asmhook2(0x473BF1, code2)  -- Non-Flat Water (not used)
hooks.asmhook2(0x473CD1, code1)  -- Non-Flat Water (not used)
hooks.asmhook2(0x473CFB, code2)  -- Non-Flat Water (not used)
hooks.asmhook2(0x474095, code1)  -- Non-Flat Water (not used)
hooks.asmhook2(0x4740C3, code2)  -- Non-Flat Water (not used)

-- indoor facet
hooks.asmpatch(0x4953BE, "lea eax, [esi+0xF]", 3)

-- indoor water facet
hooks.asmpatch(0x492D76, "lea edx, [eax+0xF]", 3)

-- indoor sky facet
hooks.asmpatch(0x4933F7, "mov eax, 0xF", 5)
hooks.asmpatch(0x4933FC, "shl ecx, 0xF", 3)
hooks.asmpatch(0x49357E, "shr edx, 0xF", 3)
hooks.asmpatch(0x4935B7, "shr edx, 0xF", 3)
hooks.asmpatch(0x493650, "shr edx, 0xF", 3)
hooks.asmpatch(0x4936B9, "shr edx, 0xF", 3)
