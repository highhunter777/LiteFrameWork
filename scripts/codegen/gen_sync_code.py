# -*- coding: utf-8 -*-
# Sync code generator (LiteSim <-> wire)
#
# Why this exists: adding one field to EntitySlot used to mean editing SIX places
# (struct + two checksum profiles + snapshot entry + proto + codec). Miss one and
# nothing fails to compile -- you get a silent divergence instead. That is not
# hypothetical: CorpseFrames shipped with "public" in its comment, "public" in the
# public checksum, and NO wire field, which made every reconcile in the 180-frame
# corpse window a false one.
#
# The single source of truth is the [StateLayer] attribute on each field. This
# script parses the struct SOURCE (not the compiled assembly) and emits the
# mechanical half of the sync plumbing as a PARTIAL struct, so hand-written files
# keep their shape and the field->wire mapping lives in exactly one place.
#
# WHY SOURCE AND NOT REFLECTION: SimChecksum calls the generated mixers, and the
# generated mixers live in EntitySlot -- so the assembly cannot build before the
# file is generated. Reflecting over the assembly is a chicken-and-egg deadlock.
# Parsing source breaks it and has no build-order coupling.
#
# Toolbox entry:  python scripts/codegen/toolbox.py sync-code
# Direct entry:   python scripts/codegen/gen_sync_code.py
# Output:         Assets/LiteSim/Core/Diagnostics/Generated/EntitySlot.Sync.g.cs
#                 (checked in -- same convention as Battle.cs / BuildHash.g.cs;
#                  CRLF line endings -- matches the checked-in artifact)
# Rerun when:     a [StateLayer]-decorated struct/field changed (then rerun build-hash).

import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _codegen import info, repo_root, run_tool

TOOL = 'sync-code'
ROOT = repo_root()
OUT_FILE = os.path.join(ROOT, 'Assets', 'LiteSim', 'Core', 'Diagnostics', 'Generated', 'EntitySlot.Sync.g.cs')

# Structs covered by generation. Slot-index layering (see below) applies to
# ActionRuntime, whose public/private split depends on the SUB-SLOT INDEX:
# slot 0 is the public action summary, slots 1..3 are the private skill ledger.
# The three kind tables (ItemState/ProjectileState/ZoneState) are world-visible
# faces folded identically into BOTH profiles, like EntitySlot.
TARGETS = [
    ('EntitySlot',      'Assets/LiteSim/Core/Scripts/EntitySlot.cs'),
    ('WeaponRuntime',   'Assets/LiteSim/Core/Scripts/SimCombatRuntime.cs'),
    ('ActionRuntime',   'Assets/LiteSim/Core/Scripts/SimCombatRuntime.cs'),
    ('StatusSlotData',  'Assets/LiteSim/Core/Scripts/SimCombatRuntime.cs'),
    ('MatchBagSlot',    'Assets/LiteSim/Core/Scripts/SimMatchRuntime.cs'),
    ('ItemState',       'Assets/LiteSim/Core/Scripts/EntityKindTables.cs'),
    ('ProjectileState', 'Assets/LiteSim/Core/Scripts/EntityKindTables.cs'),
    ('ZoneState',       'Assets/LiteSim/Core/Scripts/EntityKindTables.cs'),
]


def parse_state_layer(text):
    """Parse one [StateLayer(...)] attribute line."""
    m = re.search(r'StateLayer\.(Public|Private|Internal)', text)
    if not m:
        raise ValueError(f'cannot parse layer from: {text}')
    layer = m.group(1)

    flat = None
    m = re.search(r'flatten:\s*new\[\]\s*\{([^}]*)\}', text)
    if m:
        flat = [p.strip().strip('"') for p in m.group(1).split(',') if p.strip()]

    wire = None
    m = re.search(r'wireType:\s*"([^"]+)"', text)
    if m:
        wire = m.group(1)

    # Slot-index condition: some structs are layered BY ARRAY POSITION (ActionRuntime:
    # slot 0 is the public action summary, slots 1..3 are private skill ledgers).
    # That cannot be inferred from the type -- it must be declared.
    only_slot = -1
    m = re.search(r'onlySlotIndex:\s*(-?\d+)', text)
    if m:
        only_slot = int(m.group(1))

    otherwise = 'Private'
    m = re.search(r'otherwiseLayer:\s*StateLayer\.(\w+)', text)
    if m:
        otherwise = m.group(1)

    return {'Layer': layer, 'Flatten': flat, 'Wire': wire, 'OnlySlot': only_slot, 'Otherwise': otherwise}


# Collect [StateLayer]-decorated fields of one struct, in DECLARATION order.
def struct_fields(file_path, struct_name):
    if not os.path.isfile(file_path):
        raise ValueError(f'source not found: {file_path}')
    with open(file_path, 'r', encoding='utf-8-sig') as f:
        source_lines = f.read().splitlines()

    inside = False
    pending = None
    result = []
    struct_start = re.compile(r'^(public|internal)\s+(partial\s+)?struct\s+' + re.escape(struct_name) + r'\b')
    field_line = re.compile(r'^public\s+([A-Za-z_][\w.]*)\s+([A-Za-z_]\w*)\s*;')

    for raw in source_lines:
        line = raw.strip()

        if not inside:
            if struct_start.match(line):
                inside = True
            continue

        if line == '}':
            break  # end of struct body

        if line.startswith('[StateLayer('):
            pending = parse_state_layer(line)
            continue
        if pending is None:
            continue

        m = field_line.match(line)
        if m:
            result.append({'Type': m.group(1), 'Name': m.group(2), **pending})
        pending = None

    if not result:
        raise ValueError(f"no [StateLayer] fields found in struct '{struct_name}' ({file_path})")
    return result


def mix_call(type_name, value_expr, field_name):
    # Map a field (or one flattened component) to its mixer call.
    # Resolution order: explicit scalar type > attribute wireType > declared type.
    scalars = {
        # C# keyword spellings (source-parsed)
        'long': 'MixInt64', 'int': 'MixInt32', 'uint': 'MixUInt32', 'byte': 'MixByte', 'ulong': 'MixUInt64',
        # BCL spellings (kept for flattened components / explicit wire types)
        'Int64': 'MixInt64', 'Int32': 'MixInt32', 'UInt32': 'MixUInt32', 'Byte': 'MixByte',
        'Single': 'MixFloat', 'float': 'MixFloat',
        'int32': 'MixInt32', 'uint32': 'MixUInt32', 'int64': 'MixInt64', 'uint64': 'MixUInt64',
    }
    if type_name in scalars:
        return f'h = SimChecksum.{scalars[type_name]}(h, {value_expr});'
    # enum names end with 'State' / 'Phase' / 'Kind' in this codebase; treat
    # anything not a known scalar as an enum carried as a byte
    if re.search(r'(State|Phase|Kind|Flags)$', type_name):
        return f'h = SimChecksum.MixByte(h, (byte){value_expr});'
    raise ValueError(f"no mixer for type '{type_name}' on field '{field_name}' -- add a wireType to its [StateLayer]")


def component_type(composite_type, field_name):
    # Scalar type of a flattened component (SimVector3 -> three Single values).
    if composite_type == 'SimVector3':
        return 'Single'
    raise ValueError(f"no flatten rule for composite '{composite_type}' on field '{field_name}'")


def write_profile(out, struct_name, method_name, doc, layers, fields,
                  value_expr_prefix='e', slot_expr=None, slot_parm=None):
    # Emit one fold method for one struct.
    #   value_expr_prefix : how to reach the value, e.g. 'e' (slot) or 'a' (array item)
    #   slot_expr         : expression for the sub-slot index, or None when the struct
    #                       has no slot-indexed layering (most of them)
    out.append(f'        /// <summary>{doc}</summary>')
    if slot_parm:
        sig = f'internal static uint {method_name}(uint h, in {struct_name} {value_expr_prefix}, int {slot_parm})'
    else:
        sig = f'internal static uint {method_name}(uint h, in {struct_name} {value_expr_prefix})'
    out.append(f'        {sig}')
    out.append('        {')

    for f in fields:
        effective = f['Layer']

        if f['OnlySlot'] >= 0 and slot_expr:
            # Slot-indexed layering: the SAME field belongs to different layers
            # depending on the sub-slot index.
            #   public profile -> fold only for the designated slot (else it would
            #                     leak private skill data into the reconcile anchor)
            #   full profile   -> fold unconditionally for every slot (it is a
            #                     superset; skipping here would drop deterministic state)
            in_layers = f['Layer'] in layers
            out_layers = f['Otherwise'] in layers

            if in_layers and out_layers:
                # full profile reaches this field through both layers -> no branch needed
                out.append('            ' + mix_call(f['Type'], f'{value_expr_prefix}.{f["Name"]}', f['Name']))
                continue
            if not in_layers and not out_layers:
                continue

            # NOTE: slot_expr is a C# expression (e.g. `slot`). Build the condition with
            # the C# variable name, not a Python-interpolated value.
            csharp_slot = slot_expr.lstrip('$')
            cond = f'{csharp_slot} == {f["OnlySlot"]}'
            neg_cond = f'{csharp_slot} != {f["OnlySlot"]}'
            use = cond if in_layers else neg_cond
            out.append(f'            if ({use})')
            out.append('            {')
            out.append('                ' + mix_call(f['Type'], f'{value_expr_prefix}.{f["Name"]}', f['Name']))
            out.append('            }')
            continue

        if effective not in layers:
            continue

        if f['Flatten']:
            comp = component_type(f['Type'], f['Name'])
            for part in f['Flatten']:
                out.append('            ' + mix_call(comp, f'{value_expr_prefix}.{f["Name"]}.{part}', f['Name']))
            continue

        # NOTE: checksum folds by the DECLARED type, not by wireType. wireType only
        # describes how proto carries the value (byte -> uint32); folding by it would
        # change every existing hash for no reason.
        out.append('            ' + mix_call(f['Type'], f'{value_expr_prefix}.{f["Name"]}', f['Name']))

    out.append('            return h;')
    out.append('        }')
    out.append('')


def build():
    # -- scan: collect fields for every target struct --
    struct_fields_map = {}
    for name, src in TARGETS:
        struct_fields_map[name] = struct_fields(os.path.join(ROOT, src), name)

    out = [
        '// <auto-generated>',
        '//     Generated by scripts/codegen/gen_sync_code.py -- DO NOT EDIT.',
        '//     Source of truth: [StateLayer] attributes on the fields themselves.',
        '//     The field declarations stay hand-written; this is the mechanical half of',
        '//     the checksum plumbing. Adding a field = edit ONE place.',
        '// </auto-generated>',
        '',
        'namespace LiteSim',
        '{',
    ]

    for name, _src in TARGETS:
        flds = struct_fields_map[name]

        out.append(f'    public partial struct {name}')
        out.append('    {')

        # EntitySlot and the kind tables are single values (no sub-slot); the
        # array-backed runtimes pass the sub-slot index in so slot-indexed
        # layering can be expressed.
        if name in ('EntitySlot', 'ItemState', 'ProjectileState', 'ZoneState'):
            write_profile(out, name, 'MixPublic',
                          'Public-profile checksum fold (order = field declaration order).',
                          ['Public'], flds)
            write_profile(out, name, 'MixFull',
                          'Full-profile fold (ordered by enum value; iterates all sub-slots).',
                          ['Public', 'Private', 'Internal'], flds)
        elif name == 'ActionRuntime':
            # slot-indexed: sub-slot 0 is the public summary, 1..3 are private.
            # The loop covers all sub-slots; the branch picks public vs private per slot.
            write_profile(out, name, 'MixPublic',
                          'Public-profile fold for one sub-slot (0 = action summary; 1..3 contribute nothing).',
                          ['Public'], flds, value_expr_prefix='a', slot_expr='$slot', slot_parm='slot')
            write_profile(out, name, 'MixFull',
                          'Full-profile fold for one sub-slot (all deterministic fields).',
                          ['Public', 'Private', 'Internal'], flds, value_expr_prefix='a', slot_expr='$slot', slot_parm='slot')
        else:
            write_profile(out, name, 'MixFull',
                          'Full-profile fold for one element (all deterministic fields).',
                          ['Public', 'Private', 'Internal'], flds, value_expr_prefix='a')

        out.append('    }')
        out.append('')

    out.append('}')
    # CRLF -- the checked-in artifact was written line-ending-stable on Windows;
    # keep byte compatibility with the existing file (BuildHash normalizes anyway).
    return '\r\n'.join(out) + '\r\n'


def main():
    new = build()
    rel = os.path.relpath(OUT_FILE, ROOT).replace('\\', '/')
    old = open(OUT_FILE, 'r', encoding='utf-8-sig', newline='').read() if os.path.isfile(OUT_FILE) else ''
    if new == old:
        info(TOOL, f'up to date: {rel}')
        return 0
    os.makedirs(os.path.dirname(OUT_FILE), exist_ok=True)
    with open(OUT_FILE, 'w', encoding='utf-8', newline='') as f:
        f.write(new)
    info(TOOL, f'generated: {rel}')
    return 0


if __name__ == '__main__':
    run_tool(TOOL, main)
