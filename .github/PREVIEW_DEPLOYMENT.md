# Preview Deployment Setup

This document describes how to set up preview deployments for pull requests.

## Overview

The repository automatically builds and uploads website previews as artifacts for every pull request. Optionally, it can also deploy live preview versions as Cloudflare Workers with static assets when configured.

## Default Behavior (No Setup Required)

By default, the preview workflow (`.github/workflows/pr-preview-artifact.yml`) will:
- Build the website when PRs modify `website/**` files
- Upload the built site as a GitHub Actions artifact (30-day retention)
- Post a comment on the PR with download instructions
- Can be manually triggered via workflow dispatch

## Optional: Enable Live Cloudflare Worker Previews

To enable automatic live preview deployments, configure Cloudflare Workers:

### 1. Enable Workers

1. Sign up for a [Cloudflare account](https://dash.cloudflare.com/sign-up) (free tier is sufficient)
2. Go to **Workers & Pages** in your Cloudflare dashboard
3. Enable a `workers.dev` subdomain for the account if one is not already configured

### 2. Get Cloudflare API Credentials

1. Go to your Cloudflare dashboard
2. Navigate to **My Profile** → **API Tokens**
3. Create an API token with Editor access to the `razorconsole` Worker. Preview uploads use
   Cloudflare Worker Previews under that Worker and do not create persistent per-PR Workers.
4. Copy your **Account ID** from the Workers dashboard

### 3. Add GitHub Secrets

Add the following secrets to your GitHub repository:

1. Go to your repository on GitHub
2. Navigate to **Settings** → **Secrets and variables** → **Actions**
3. Click **New repository secret** and add:
   - `CLOUDFLARE_API_TOKEN`: Your Cloudflare API token
   - `CLOUDFLARE_ACCOUNT_ID`: Your Cloudflare account ID

### 4. Test the Workflow

1. Create a pull request with some changes to the website
2. The preview deployment workflow will automatically run
3. A comment will be posted on the PR with both the artifact download link and the live preview URL
4. Each subsequent push to the PR will update both the artifact and the live preview

## How It Works

- The `.github/workflows/pr-preview-artifact.yml` workflow triggers on pull request events or manual dispatch
- It builds the website using the same process as the production build
- The built site is always uploaded as a GitHub Actions artifact
- If Cloudflare secrets are configured, it also uploads the static artifact as an isolated Preview
  under the `razorconsole` Worker, names it `razorconsole-pr-<number>`, and reports its temporary URL
- A bot comment is added/updated on the PR with download and/or live preview information
- The preview is automatically updated when new commits are pushed

## Manual Invoke

The workflow can be manually triggered via the GitHub Actions UI:
1. Go to the **Actions** tab in your repository
2. Select the "Deploy Preview" workflow
3. Click **Run workflow**

## Alternative Services

If you prefer not to use Cloudflare Workers, you can:

1. Use GitHub Pages with separate branches (requires additional configuration)
2. Use Netlify, Vercel, or another similar service (modify the workflow accordingly)
3. Use only the artifact upload (default behavior, no setup needed)
