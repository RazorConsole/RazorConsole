import { useState, useRef, useEffect } from "react"
import { cn } from "@/lib/utils"
import { getOptimizedImageUrl } from "@/lib/image-utils"
import { ImageIcon } from "lucide-react"

interface ImageProps extends React.ImgHTMLAttributes<HTMLImageElement> {
    width?: number
    height?: number
    containerClassName?: string
}

export function Image({
    src,
    alt,
    width,
    height,
    className,
    containerClassName,
    ...props
}: ImageProps) {
    const [isLoading, setIsLoading] = useState(true)
    const [hasError, setHasError] = useState(false)
    const [prevSrc, setPrevSrc] = useState(src) 
    const imgRef = useRef<HTMLImageElement>(null)

    if (src !== prevSrc) {
        setPrevSrc(src)
        setIsLoading(true)
        setHasError(false)
    }

    const optimizedSrc = src ? getOptimizedImageUrl(src, { size: width }) : ""

    useEffect(() => {
        if (imgRef.current?.complete) {
            setIsLoading(false)
        }
    }, [optimizedSrc]) 

    return (
        <div className={cn(
            "relative overflow-hidden bg-slate-100 dark:bg-slate-800",
            containerClassName
        )}>
            {/* Skeleton */}
            {isLoading && !hasError && (
                <div className="absolute inset-0 overflow-hidden bg-slate-200 dark:bg-slate-800">
                    <div className="absolute inset-0 animate-pulse bg-slate-200 dark:bg-slate-700" />
                    <div className="animate-shimmer absolute inset-0 -translate-x-full bg-linear-to-r from-transparent via-white/20 dark:via-white/5 to-transparent shadow-2xl"
                        style={{ width: '200%' }}
                    />
                </div>
            )}

            {hasError && (
                <div className="flex h-full w-full flex-col items-center justify-center gap-2 bg-slate-100 text-slate-400 dark:bg-slate-900">
                    <ImageIcon className="h-8 w-8 animate-bounce opacity-50" />
                </div>
            )}

            <img
                {...props}
                ref={imgRef}
                src={optimizedSrc}
                alt={alt}
                width={width}
                height={height}
                className={cn(
                    "h-full w-full object-cover transition-opacity duration-300",
                    isLoading ? "opacity-0" : "opacity-100",
                    className
                )}
                onLoad={() => setIsLoading(false)}
                onError={() => {
                    setIsLoading(false)
                    setHasError(true)
                }}
            />
        </div>
    )
}