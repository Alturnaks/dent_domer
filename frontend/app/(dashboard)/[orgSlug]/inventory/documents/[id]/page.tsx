"use client";

import { useParams } from "next/navigation";
import { DocumentEditor } from "@/components/inventory/document-editor";

export default function StockDocumentPage() {
  const { id } = useParams<{ id: string }>();
  return <DocumentEditor key={id} id={id} />;
}
