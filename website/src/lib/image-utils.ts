const getGithubAvatar = (username: string, size: number) =>
    `https://avatars.githubusercontent.com/${username}?s=${size}&v=4`;

const getGitlabAvatar = (username: string, size: number) =>
    `https://gitlab.com/${username}.png?width=${size}`;

const getBitbucketAvatar = (username: string, size: number) =>
    `https://bitbucket.org/account/${username}/avatar/${size}/`;


const getStaticallyCdn = (url: string, width?: number) => {
    let processedUrl = url;

    if (url.includes('raw.githubusercontent.com')) {
        processedUrl = url.replace('raw.githubusercontent.com', 'cdn.statically.io/gh');
    } else if (url.includes('gitlab.com') && url.includes('/raw/')) {
        processedUrl = url.replace('gitlab.com', 'cdn.statically.io/gl').replace('/-/raw/', '/');
    }

    if (width) {
        const cleanUrl = processedUrl.replace(/^https?:\/\//, '');
        return `https://cdn.statically.io/img/${cleanUrl}?w=${width}&f=auto`;
    }

    return processedUrl;
};

const getCompressedUrl = (url: string, width: number) => {
    const isAnimated = url.toLowerCase().endsWith('.gif') || url.toLowerCase().endsWith('.webp');

    if (isAnimated) {
        return getStaticallyCdn(url);
    }

    const params = new URLSearchParams({
        url: url.startsWith('http') ? url : `https://${url}`,
        w: width.toString(),
        output: 'webp',
        q: '80'
    });

    return `https://wsrv.nl/?${params.toString()}`;
};
export function getOptimizedImageUrl(url: string, options?: { size?: number }) {
    if (!url) return url;

    const width = options?.size;
    const isRaw = url.includes('/raw/') || url.includes('raw.githubusercontent');

    if (!isRaw) {
        if (url.includes('github.com')) {
            const user = url.split('/').filter(Boolean).pop()?.replace('.png', '');
            return getGithubAvatar(user!, width ?? 96);
        }
        if (url.includes('gitlab.com')) {
            const user = url.split('/').filter(Boolean).pop();
            return getGitlabAvatar(user!, width ?? 96);
        }
        if (url.includes('bitbucket.org')) {
            const user = url.split('/').filter(Boolean).pop();
            return getBitbucketAvatar(user!, width ?? 96);
        }
    }

    if (width) {
        return getCompressedUrl(url, width);
    }

    return getStaticallyCdn(url);
}