import { ChevronLeft, ChevronRight } from "lucide-react"
import { useState } from "react"

export default function ImageBanner({ imageUrls, alt }: { imageUrls: string[]; alt: string }) {
  const [currentIndex, setCurrentIndex] = useState(0)

  if (imageUrls.length === 0) return null

  const goToPrevious = (e: React.MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()
    setCurrentIndex((prev) => (prev === 0 ? imageUrls.length - 1 : prev - 1))
  }

  const goToNext = (e: React.MouseEvent) => {
    e.preventDefault()
    e.stopPropagation()
    setCurrentIndex((prev) => (prev === imageUrls.length - 1 ? 0 : prev + 1))
  }

  return (
    <div className="group relative h-72 overflow-hidden rounded-t-lg">
      <img
        src={imageUrls[currentIndex]}
        alt={`${alt} screenshot ${currentIndex + 1}`}
        className="h-full w-full object-cover"
        loading="lazy"
      />
      {imageUrls.length > 1 && (
        <>
          <button
            onClick={goToPrevious}
            className="absolute top-1/2 left-2 -translate-y-1/2 rounded-full bg-black/50 p-1 text-white opacity-0 transition-opacity group-hover:opacity-100 hover:bg-black/70"
            aria-label="Previous image"
          >
            <ChevronLeft className="h-4 w-4" />
          </button>
          <button
            onClick={goToNext}
            className="absolute top-1/2 right-2 -translate-y-1/2 rounded-full bg-black/50 p-1 text-white opacity-0 transition-opacity group-hover:opacity-100 hover:bg-black/70"
            aria-label="Next image"
          >
            <ChevronRight className="h-4 w-4" />
          </button>
          <div className="absolute bottom-2 left-1/2 flex -translate-x-1/2 gap-1">
            {imageUrls.map((_, index) => (
              <button
                key={index}
                onClick={(e) => {
                  e.preventDefault()
                  e.stopPropagation()
                  setCurrentIndex(index)
                }}
                className={`h-2 w-2 rounded-full transition-colors ${
                  index === currentIndex ? "bg-white" : "bg-white/50"
                }`}
                aria-label={`Go to image ${index + 1}`}
              />
            ))}
          </div>
        </>
      )}
    </div>
  )
}
