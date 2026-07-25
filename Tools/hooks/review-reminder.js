#!/usr/bin/env node
/*
 * PostToolUse hook — fires after Write/Edit/MultiEdit.
 *
 * This project has no compiler in the loop: Unity is the compiler, and mistakes only surface once
 * the editor recompiles. Several bugs here were found the expensive way — edit, run, watch it break,
 * edit again. The rule is review-then-execute, and this hook is what keeps it from being forgotten.
 *
 * Only C# files trigger it. Docs, JSON and shell edits pass through silently.
 *
 * Reads the hook payload on stdin, writes a JSON object on stdout whose additionalContext is injected
 * back into the model's context. Any parse failure exits quietly: a broken hook must never wedge a turn.
 */

let raw = '';

process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => { raw += chunk; });
process.stdin.on('end', () => {
  let filePath = '';

  try {
    const payload = JSON.parse(raw);
    filePath =
      (payload.tool_response && payload.tool_response.filePath) ||
      (payload.tool_input && payload.tool_input.file_path) ||
      '';
  } catch {
    return; // Malformed payload — say nothing rather than derail the turn.
  }

  if (!/\.cs$/i.test(filePath)) return;

  const reminder = [
    'C# changed: ' + filePath,
    '',
    'Review before executing. Do NOT run Unity menu items, enter play mode, or start a build until:',
    '  1. bash Tools/check-project.sh          — FindProperty targets, Unity 6.5 API removals,',
    '                                            manifest/namespace drift, min SDK floor',
    '  2. re-read the edit                     — does it hold together with the callers you did not touch?',
    '  3. RequestScriptCompilation, then read_console  — Unity will not have recompiled on its own,',
    '                                            and a stale assembly makes the next run test old code',
    '',
    'If several fixes are queued, finish and review them together — one recompile, one verification pass.',
  ].join('\n');

  process.stdout.write(JSON.stringify({
    hookSpecificOutput: {
      hookEventName: 'PostToolUse',
      additionalContext: reminder,
    },
  }));
});
