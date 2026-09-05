<script setup>
defineProps({ photo: Object, photoUrl: String });
</script>

<template>
  <section class="result-section" aria-labelledby="result-heading">
    <h2 id="result-heading">抓拍结果</h2>
    <div class="result-content">
      <div class="photo-frame">
        <img v-if="photo" :src="photoUrl" alt="最新抓拍照片" />
        <div v-else class="photo-empty">
          <svg viewBox="0 0 48 48" aria-hidden="true"><rect x="5" y="5" width="38" height="38" rx="2" /><circle cx="16" cy="16" r="4" /><path d="m7 38 11-12 8 8 7-10 9 10" /></svg>
          <span>暂无照片</span>
        </div>
      </div>
      <div class="result-details">
        <dl>
          <div><dt>图片尺寸</dt><dd>{{ photo ? `${photo.width} × ${photo.height}` : '—' }}</dd></div>
          <div><dt>文件大小</dt><dd>{{ photo ? `${(photo.size / 1024).toFixed(1)} KB` : '—' }}</dd></div>
        </dl>
        <a v-if="photo" class="button secondary download-button" :href="photoUrl" download="face-capture.jpg">下载 JPEG</a>
        <button v-else class="download-button" disabled>下载 JPEG</button>
      </div>
    </div>
  </section>
</template>
