export default function VideoBanner({ src, title }: { src: string; title: string }) {
  return (
    <div className="h-72 overflow-hidden rounded-t-lg bg-slate-950">
      <video
        className="h-full w-full object-contain"
        autoPlay
        controls
        loop
        muted
        playsInline
        preload="metadata"
        aria-label={`${title} gameplay demo`}
      >
        <source src={src} type="video/mp4" />
        Your browser does not support embedded video.
      </video>
    </div>
  )
}
