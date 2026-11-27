# Agent Rules

## General

- Never include any co-authoring attribution anywhere (comments, commit messages, PRs and so on)
- Never push without explicit approval
- All major changes, additions and decisions in the project must be noted in `.\CHANGELOG.md`

## Files and Source of Truth

- Never edit files outside of the directory which contains this file
- All code for the application lives in `.\src\`. The only exclusions are are Dockerfiles and other pipeline-related files, which may live in the repository root
