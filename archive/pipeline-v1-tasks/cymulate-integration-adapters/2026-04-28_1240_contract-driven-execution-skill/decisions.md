* Create a new task directory because the corrected request targets a different skill than the previous pass.
* Keep the skill concise and procedural, with no scripts/references/assets unless the workflow needs them.
* Use `init_skill.py` to create required skill structure and `agents/openai.yaml`.
* Remove angle-bracket placeholders from the frontmatter description because `quick_validate.py` rejects them.
* Use the bundled Codex Python runtime for validation because it has the required `PyYAML` dependency.
