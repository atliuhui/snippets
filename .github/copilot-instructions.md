## Language Rule

**English is the primary language for this project. All documentation, comments, and code strings MUST be in English.**

This includes:
- Code comments and docstrings
- Commit messages
- README files and markdown documentation
- UI strings and error messages
- Git branch names (use kebab-case)

## Git Commit Rule

**Default: Do not stage, unstage, commit, or alter the git index unless explicitly requested.**

This prevents:
- Staging files before the user has reviewed them
- Accidentally including unrelated or unwanted changes
- Messy commit messages from premature timing

### Default workflow
1. Agent completes code changes
2. Agent stops; leaves all changes unstaged
3. User reviews, runs `git add` and `git commit` manually
4. User optionally runs `git push`

### When the user asks the Agent to commit

If the user explicitly requests a commit (e.g. "commit it"), the Agent should:

1. Run `git --no-pager status` and `git --no-pager diff` to inspect the actual changes.
2. Generate a commit message based strictly on the diff:
   - Subject: conventional-commit style (`feat:`, `fix:`, `refactor:`, `chore:`, `docs:`), <= 72 chars.
   - Body (optional): brief explanation of *what* and *why*, only when non-obvious.
   - Do NOT invent changes that are not in the diff.
   - Do NOT add Co-authored-by trailers.
3. Stage all relevant files with `git add` and commit with the generated message.
4. Print the resulting commit hash and subject for confirmation.

The Agent should NOT commit if:
- The user did not explicitly request a commit.
- The staged/working changes mix unrelated work — ask the user to clarify first.
