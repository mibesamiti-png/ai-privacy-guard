"""Render source-free geometry diagrams of model boxes from real media.
Copyrighted/personally identifiable source imagery never leaves CI.
"""
import argparse, json
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
def run(root):
    report=root/"report"/"results.json"
    if not report.exists():
        raise FileNotFoundError(report)
    data=json.loads(report.read_text(encoding="utf-8"))
    out=root/"geometry_maps"
    out.mkdir(parents=True,exist_ok=True)
    summary=[]
    for case in data["results"]:
        W,H=420,520
        im=Image.new("RGB",(W,H),(21,27,38))
        d=ImageDraw.Draw(im)
        pw=int(case["width"]);ph=int(case["height"])
        scale=min((W-32)/pw,(H-95)/ph)
        ox=(W-pw*scale)/2;oy=60+(H-95-ph*scale)/2
        x1=int(ox);y1=int(oy);x2=int(ox+pw*scale);y2=int(oy+ph*scale)
        d.rectangle((x1,y1,x2,y2),outline=(139,154,177),width=2)
        d.text((14,14),case["id"],fill="white")
        d.text((14,36),f"{case['count']} candidate(s) | Source image not copied",fill=(190,204,222))
        for detection in case["detections"]:
            x,y,w,h=detection["normalized_xywh"]
            box=(int(x1+x*(x2-x1)),int(y1+y*(y2-y1)),
                 int(x1+(x+w)*(x2-x1)),int(y1+(y+h)*(y2-y1)))
            d.rectangle(box,outline=(255,105,80),width=3)
            d.text((max(6,box[0]),max(62,box[1]-16)),f"{detection['category']} {detection['confidence']:.2f}",fill=(255,191,154))
        path=out/(case["id"]+"_boxes_only.png")
        im.save(path)
        summary.append({"id":case["id"],"map":str(path.relative_to(root)),"count":case["count"]})
    (root/"maps_index.json").write_text(json.dumps(summary,indent=2),encoding="utf-8")
    print("GEOMETRY_MAPS_PASS",len(summary),"source-free annotated diagrams")
if __name__=="__main__":
    ap=argparse.ArgumentParser();ap.add_argument("--root",required=True)
    args=ap.parse_args();run(Path(args.root))
